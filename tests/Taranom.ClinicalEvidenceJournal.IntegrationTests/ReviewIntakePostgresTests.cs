using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class ReviewIntakePostgresTests
{
    private static readonly Lazy<Task> Ready = new(() => ReviewJournalTestSchema.EnsureAsync());
    private static string ConnectionString => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG required for synthetic CI PostgreSQL");

    private static string Center(string prefix = "center") =>
        "test:" + prefix + "-" + Guid.NewGuid().ToString("N");
    private const string Doctor = "test:doctor-a";
    private const string Manager = "test:manager-a";
    private const string Actor = "test:untrusted-submitter";
    private const string Proof = "test:unverified-reference";

    private static EvidenceSubmission Command(string center, string subject,
        EvidenceSubjectKind subjectKind, EvidenceKind kind, long version = 0) =>
        new(Guid.NewGuid(), version, center, subject, subjectKind, kind, Actor, Proof);

    private static EvidenceSubmission Physician(string center, long version = 0,
        EvidenceKind kind = EvidenceKind.PhysicianProfessionalQualificationClaim) =>
        Command(center, Doctor, EvidenceSubjectKind.Physician, kind, version);

    private static EvidenceSubmission Representative(string center, long version = 0) =>
        Command(center, Manager, EvidenceSubjectKind.CenterRepresentative,
            EvidenceKind.CenterManagerAuthorityClaim, version);

    private static async Task<(NpgsqlDataSource Db, PgClinicalEvidenceJournal Store)> ConnectAsync()
    {
        await Ready.Value.WaitAsync(TestContext.Current.CancellationToken);
        var db = NpgsqlDataSource.Create(ConnectionString);
        return (db, new PgClinicalEvidenceJournal(db));
    }

    private static Task<EvidenceIntakeDecision> Submit(PgClinicalEvidenceJournal store,
        string center, string subject, EvidenceSubjectKind kind, EvidenceSubmission submission) =>
        store.SubmitAsync(center, subject, kind, submission, TestContext.Current.CancellationToken);

    private static Task<EvidenceIntakeSnapshot> Read(PgClinicalEvidenceJournal store,
        string center, string subject, EvidenceSubjectKind kind) =>
        store.ReadAsync(center, subject, kind, TestContext.Current.CancellationToken);

    private static async Task<long> Count(NpgsqlDataSource db, string table, string center,
        string? subject = null)
    {
        if (table is not ("internal_test_review_case" or "internal_test_review_event" or
            "internal_test_review_work_item" or "internal_test_review_audit" or
            "internal_test_review_denial"))
            throw new ArgumentException("Unexpected isolated test table.", nameof(table));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT count(*) FROM {table} WHERE center_ref=@center" +
            (subject is null ? "" : " AND subject_ref=@subject");
        await using var command = new NpgsqlCommand(sql, cn);
        command.Parameters.AddWithValue("center", center);
        if (subject is not null) command.Parameters.AddWithValue("subject", subject);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingReviewCaseReadsEmptyWithoutCreatingData()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var state = await Read(store, center, Doctor, EvidenceSubjectKind.Physician);
        Assert.Equal(0, state.Revision);
        Assert.Empty(state.Submissions);
        Assert.Empty(state.ReviewWorkItems);
        Assert.Equal(0L, await Count(db, "internal_test_review_case", center));
    }

    [Fact]
    public async Task PhysicianQualificationAndContractRemainTwoPendingReviews()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated,
            (await Submit(store, center, Doctor, EvidenceSubjectKind.Physician,
                Physician(center))).Outcome);
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated,
            (await Submit(store, center, Doctor, EvidenceSubjectKind.Physician,
                Physician(center, 1, EvidenceKind.PhysicianTaranomServiceContractClaim))).Outcome);
        var state = await Read(store, center, Doctor, EvidenceSubjectKind.Physician);
        Assert.Equal(2L, state.Revision);
        Assert.Equal(2, state.ReviewWorkItems.Length);
        Assert.Equal(EvidenceKind.PhysicianProfessionalQualificationClaim,
            state.ReviewWorkItems[0].EvidenceKind);
        Assert.Equal(EvidenceKind.PhysicianTaranomServiceContractClaim,
            state.ReviewWorkItems[1].EvidenceKind);
        Assert.Equal(2L, await Count(db, "internal_test_review_event", center));
        Assert.Equal(2L, await Count(db, "internal_test_review_audit", center));
        Assert.Equal(2L, await Count(db, "internal_test_review_work_item", center));
    }

    [Fact]
    public async Task ManagerDelegationClaimNeverVerifiesTheManager()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var result = await Submit(store, center, Manager,
            EvidenceSubjectKind.CenterRepresentative, Representative(center));
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated, result.Outcome);
        Assert.Equal(EvidenceKind.CenterManagerAuthorityClaim,
            Assert.Single(result.Snapshot.ReviewWorkItems).EvidenceKind);
        Assert.DoesNotContain(Enum.GetNames<EvidenceIntakeOutcome>(),
            item => item is "Approved" or "Authorized" or "Verified");
    }

    [Fact]
    public async Task OneCentersRecordCannotBeModifiedFromAnotherCenter()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var a = Center();
        var b = Center();
        var decision = await Submit(store, b, Doctor, EvidenceSubjectKind.Physician, Physician(a));
        Assert.Equal(EvidenceIntakeOutcome.ScopeDenied, decision.Outcome);
        Assert.Equal(0L, await Count(db, "internal_test_review_case", b));
        Assert.Equal(1L, await Count(db, "internal_test_review_denial", b));
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated,
            (await Submit(store, a, Doctor, EvidenceSubjectKind.Physician,
                Physician(a))).Outcome);
        Assert.Empty((await Read(store, b, Doctor, EvidenceSubjectKind.Physician)).Submissions);
    }

    [Fact]
    public async Task IdenticalDoctorSyntheticReferenceDoesNotShareCentersReviewHistory()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var a = Center();
        var b = Center();
        await Submit(store, a, Doctor, EvidenceSubjectKind.Physician, Physician(a));
        await Submit(store, b, Doctor, EvidenceSubjectKind.Physician, Physician(b));
        Assert.Single((await Read(store, a, Doctor, EvidenceSubjectKind.Physician)).Submissions);
        Assert.Single((await Read(store, b, Doctor, EvidenceSubjectKind.Physician)).Submissions);
        Assert.Equal(1L, await Count(db, "internal_test_review_case", a, Doctor));
        Assert.Equal(1L, await Count(db, "internal_test_review_case", b, Doctor));
    }

    [Fact]
    public async Task ExactReplayAfterSecondClaimDoesNotAppend()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var first = Physician(center);
        await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, first);
        await Submit(store, center, Doctor, EvidenceSubjectKind.Physician,
            Physician(center, 1, EvidenceKind.PhysicianTaranomServiceContractClaim));
        Assert.Equal(EvidenceIntakeOutcome.Replayed,
            (await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, first)).Outcome);
        Assert.Equal(2L, await Count(db, "internal_test_review_event", center));
        Assert.Equal(0L, await Count(db, "internal_test_review_denial", center));
    }

    [Fact]
    public async Task ChangedReplayWritesOnlyDenialAudit()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var first = Physician(center);
        await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, first);
        var conflict = first with { OpaqueEvidenceRef = "test:changed-claim" };
        Assert.Equal(EvidenceIntakeOutcome.ConflictingReplay,
            (await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, conflict)).Outcome);
        Assert.Equal(1L, await Count(db, "internal_test_review_event", center));
        Assert.Equal(1L, await Count(db, "internal_test_review_denial", center));
    }

    [Fact]
    public async Task StaleRevisionCannotWriteNewClaim()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center));
        var old = Physician(center, 0, EvidenceKind.PhysicianTaranomServiceContractClaim);
        Assert.Equal(EvidenceIntakeOutcome.StaleRevision,
            (await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, old)).Outcome);
        Assert.Equal(1L, await Count(db, "internal_test_review_event", center));
        Assert.Equal(1L, await Count(db, "internal_test_review_denial", center));
    }

    [Fact]
    public async Task TwoConcurrentWritersWithSameExpectedVersionCannotBothCommit()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var results = await Task.WhenAll(
            Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center)),
            Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center)));
        Assert.Single(results, x => x.Outcome == EvidenceIntakeOutcome.ReviewWorkItemCreated);
        Assert.Single(results, x => x.Outcome == EvidenceIntakeOutcome.StaleRevision);
        Assert.Equal(1L, await Count(db, "internal_test_review_event", center));
        Assert.Equal(1L, await Count(db, "internal_test_review_work_item", center));
        Assert.Equal(1L, await Count(db, "internal_test_review_denial", center));
    }

    [Fact]
    public async Task SameConcurrentRequestIdCreatesExactlyOneReview()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var command = Physician(center);
        var results = await Task.WhenAll(
            Submit(store, center, Doctor, EvidenceSubjectKind.Physician, command),
            Submit(store, center, Doctor, EvidenceSubjectKind.Physician, command));
        Assert.Single(results, x => x.Outcome == EvidenceIntakeOutcome.ReviewWorkItemCreated);
        Assert.Single(results, x => x.Outcome == EvidenceIntakeOutcome.Replayed);
        Assert.Equal(1L, await Count(db, "internal_test_review_audit", center));
        Assert.Equal(1L, await Count(db, "internal_test_review_work_item", center));
        Assert.Equal(0L, await Count(db, "internal_test_review_denial", center));
    }

    [Fact]
    public async Task InvalidEvidenceKindDoesNotCreateCaseOrReview()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var incompatible = Physician(center) with {
            EvidenceKind = EvidenceKind.CenterManagerAuthorityClaim
        };
        Assert.Equal(EvidenceIntakeOutcome.Invalid,
            (await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, incompatible)).Outcome);
        Assert.Equal(0L, await Count(db, "internal_test_review_case", center));
        Assert.Equal(1L, await Count(db, "internal_test_review_denial", center));
    }

    [Fact]
    public async Task NonSyntheticCenterOrSubjectNeverEntersDatabase()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Submit(store, "real:clinical-center", Doctor, EvidenceSubjectKind.Physician,
                Physician("real:clinical-center")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Submit(store, Center(), "national-id:doctor", EvidenceSubjectKind.Physician,
                Physician(Center())));
    }

    [Fact]
    public async Task ClaimedManagerIsNotTheLicensedPhysician()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var wrong = Command(center, Manager, EvidenceSubjectKind.CenterRepresentative,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        Assert.Equal(EvidenceIntakeOutcome.Invalid,
            (await Submit(store, center, Manager, EvidenceSubjectKind.CenterRepresentative,
                wrong)).Outcome);
        Assert.Equal(0L, await Count(db, "internal_test_review_work_item", center));
    }

    [Fact]
    public async Task UnavailableAuditRevertsCaseEventAndWorkItemAtomically()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center("abort-audit");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center)));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal("TEST_EVIDENCE_AUDIT_UNAVAILABLE", error.MessageText);
        foreach (var table in new[] { "internal_test_review_case",
            "internal_test_review_event", "internal_test_review_work_item",
            "internal_test_review_audit" })
            Assert.Equal(0L, await Count(db, table, center));
    }

    [Fact]
    public async Task DenialAuditUnavailableThrowsAndDoesNotCreateReview()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center("abort-denial");
        var cmd = Physician(center) with { OpaqueEvidenceRef = "real:not-evidence" };
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Submit(store, center, Doctor, EvidenceSubjectKind.Physician, cmd));
        Assert.Equal("TEST_EVIDENCE_DENIAL_AUDIT_UNAVAILABLE", error.MessageText);
        Assert.Equal(0L, await Count(db, "internal_test_review_case", center));
        Assert.Equal(0L, await Count(db, "internal_test_review_denial", center));
    }

    [Fact]
    public async Task EventAndReviewWorkItemHistoryAreAppendOnlyEvenForSchemaOwner()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach (var table in new[] { "internal_test_review_event", "internal_test_review_work_item",
            "internal_test_review_audit" })
        {
            await using var cmd = new NpgsqlCommand(
                $"DELETE FROM {table} WHERE center_ref=@center", cn);
            cmd.Parameters.AddWithValue("center", center);
            var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("TEST_EVIDENCE_APPEND_ONLY", ex.MessageText);
        }
        Assert.Equal(1L, await Count(db, "internal_test_review_event", center));
    }

    [Fact]
    public async Task TamperedCurrentRevisionBlocksReadAndFurtherWrites()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center));
        await using(var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using(var change = new NpgsqlCommand("""
            UPDATE internal_test_review_case SET current_revision=0
            WHERE center_ref=@center
            """, cn))
        {
            change.Parameters.AddWithValue("center", center);
            await change.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var err = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Read(store, center, Doctor, EvidenceSubjectKind.Physician));
        Assert.Equal("TEST_EVIDENCE_HISTORY_CORRUPT", err.Message);
        var write = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center, 1)));
        Assert.Equal("TEST_EVIDENCE_HISTORY_CORRUPT", write.Message);
    }

    [Fact]
    public async Task TamperedReviewEvidenceCannotMasqueradeAsPreviousSubmission()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center));
        await using(var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using(var change = new NpgsqlCommand("""
            UPDATE internal_test_review_work_item
            SET opaque_evidence_ref='test:forged'
            WHERE center_ref=@center
            """, cn))
        {
            change.Parameters.AddWithValue("center", center);
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await change.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("TEST_EVIDENCE_APPEND_ONLY", error.MessageText);
        }
    }

    [Fact]
    public async Task MissingReviewCaseCannotBeConsideredPendingOrApproved()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        var state = await Read(store, center, Manager, EvidenceSubjectKind.CenterRepresentative);
        Assert.Empty(state.ReviewWorkItems);
        Assert.Empty(state.Submissions);
    }

    [Fact]
    public async Task ReadCanNeverReturnVerifiedOrActiveState()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var center = Center();
        await Submit(store, center, Doctor, EvidenceSubjectKind.Physician, Physician(center));
        var state = await Read(store, center, Doctor, EvidenceSubjectKind.Physician);
        Assert.Single(state.ReviewWorkItems);
        Assert.DoesNotContain(state.ReviewWorkItems,
            x => x.OpaqueEvidenceRef.Contains("approved:", StringComparison.Ordinal));
        Assert.DoesNotContain(Enum.GetNames<EvidenceIntakeOutcome>(),
            name => name is "Granted" or "Approved" or "Activated" or "Verified");
    }
}
