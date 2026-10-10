using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

// A captured snapshot is immutable test review metadata; it does NOT
// authorize a reviewer or attest any real professional qualification.
public sealed class FrozenReviewQueuePostgresTests
{
    private const string CaptureUser = "taranom_sprint12_snapshot_capture";
    private const string CapturePassword = "ephemeral_synthetic_snapshot_capture";
    private const string Claimant = "test:untrusted-claimant";
    private const string EvidenceRef = "test:pending-doc-ref";

    private static string Pg => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG required for synthetic test database");
    private static string NewCenter() =>
        "test:center-" + Guid.NewGuid().ToString("N");

    private static async Task<(NpgsqlDataSource Owner, PgClinicalEvidenceJournal Journal,
        NpgsqlDataSource Reader, PgFrozenUnverifiedReviewQueue Queue)> ConnectAsync()
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var owner = NpgsqlDataSource.Create(Pg);
        var cs = new NpgsqlConnectionStringBuilder(Pg)
        {
            Username = CaptureUser, Password = CapturePassword
        };
        var reader = NpgsqlDataSource.Create(cs.ConnectionString);
        return (owner, new PgClinicalEvidenceJournal(owner),
            reader, new PgFrozenUnverifiedReviewQueue(reader));
    }

    private static async Task SeedAsync(PgClinicalEvidenceJournal journal, string center,
        string subject, EvidenceSubjectKind subjectKind = EvidenceSubjectKind.Physician,
        EvidenceKind kind = EvidenceKind.PhysicianProfessionalQualificationClaim,
        long revision = 0)
    {
        var record = new EvidenceSubmission(Guid.NewGuid(), revision, center, subject,
            subjectKind, kind, Claimant, EvidenceRef);
        var outcome = await journal.SubmitAsync(center, subject, subjectKind,
            record, TestContext.Current.CancellationToken);
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated, outcome.Outcome);
    }

    private static Task<Guid> Capture(PgFrozenUnverifiedReviewQueue q, string center,
        EvidenceSubjectKind? filter = null) =>
        q.CaptureAsync(center, filter, TestContext.Current.CancellationToken);

    private static Task<FrozenReviewPage> Page(PgFrozenUnverifiedReviewQueue q,
        string center, Guid id, int size = 20, EvidenceSubjectKind? filter = null,
        FrozenReviewCursor? cursor = null) =>
        q.PageAsync(center, filter, id, size, cursor, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EmptyCapturedViewRemainsEmptyAfterLaterSubmission()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        var snapshot = await Capture(queue, center);
        await SeedAsync(journal, center, "test:doctor-later");
        var saved = await Page(queue, center, snapshot);
        Assert.Empty(saved.Items);
        Assert.Null(saved.NextCursor);
        var newer = await Capture(queue, center);
        Assert.Single((await Page(queue, center, newer)).Items);
    }

    [Fact]
    public async Task LaterAlphabeticallyEarlierAndLaterClaimsAreExcludedFromCapturedPages()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-b");
        await SeedAsync(journal, center, "test:doctor-d");
        var id = await Capture(queue, center);
        var one = await Page(queue, center, id, size: 1);
        Assert.Equal("test:doctor-b", Assert.Single(one.Items).SubjectRef);
        Assert.NotNull(one.NextCursor);

        await SeedAsync(journal, center, "test:doctor-a");
        await SeedAsync(journal, center, "test:doctor-c");
        await SeedAsync(journal, center, "test:doctor-z");
        var second = await Page(queue, center, id, size: 1, cursor: one.NextCursor);
        Assert.Equal("test:doctor-d", Assert.Single(second.Items).SubjectRef);
        Assert.Null(second.NextCursor);

        var newer = await Capture(queue, center);
        var newest = await Page(queue, center, newer);
        Assert.Equal(new[] { "test:doctor-a", "test:doctor-b", "test:doctor-c",
            "test:doctor-d", "test:doctor-z" },
            newest.Items.Select(x => x.SubjectRef).ToArray());
    }

    [Fact]
    public async Task MultipleVersionsOfSamePhysicianRemainDistinctInFrozenHistory()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a");
        await SeedAsync(journal, center, "test:doctor-a",
            kind: EvidenceKind.PhysicianTaranomServiceContractClaim, revision: 1);
        var id = await Capture(queue, center);
        var one = await Page(queue, center, id, 1);
        Assert.Equal(EvidenceKind.PhysicianProfessionalQualificationClaim,
            Assert.Single(one.Items).EvidenceKind);
        var two = await Page(queue, center, id, 1, cursor: one.NextCursor);
        Assert.Equal(EvidenceKind.PhysicianTaranomServiceContractClaim,
            Assert.Single(two.Items).EvidenceKind);
        Assert.Null(two.NextCursor);
    }

    [Fact]
    public async Task FilteredSnapshotDoesNotChangeWhenOtherSubjectKindArrives()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a");
        var id = await Capture(queue, center, EvidenceSubjectKind.Physician);
        await SeedAsync(journal, center, "test:manager-a",
            EvidenceSubjectKind.CenterRepresentative, EvidenceKind.CenterManagerAuthorityClaim);
        var result = await Page(queue, center, id,
            filter: EvidenceSubjectKind.Physician);
        Assert.Single(result.Items);
        Assert.Equal(EvidenceSubjectKind.Physician, result.Items[0].SubjectKind);
        await Assert.ThrowsAsync<ArgumentException>(() => Page(queue, center, id));
    }

    [Fact]
    public async Task CenterAndFilterCannotBeSwappedUsingAnotherSnapshot()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a");
        var id = await Capture(queue, center);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Page(queue, NewCenter(), id));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Page(queue, center, id, filter: EvidenceSubjectKind.Physician));
    }

    [Fact]
    public async Task CursorCannotAdvancePastSnapshotOrUseAnotherSnapshot()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a");
        await SeedAsync(journal, center, "test:doctor-b");
        var id = await Capture(queue, center);
        var other = await Capture(queue, center);
        var first = await Page(queue, center, id, 1);
        var token = Assert.IsType<FrozenReviewCursor>(first.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Page(queue, center, other, 1, cursor: token));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Page(queue, center, id, 1, cursor: token with { CenterRef = NewCenter() }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Page(queue, center, id, 1, cursor: token with { NextIndex = 999 }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Page(queue, center, id, 1, cursor: token with { NextIndex = 0 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task InvalidTechnicalPageSizeCannotBePassed(int size)
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Page(queue, NewCenter(), Guid.NewGuid(), size));
    }

    [Theory]
    [InlineData("")]
    [InlineData("real:medical")]
    [InlineData("test:")]
    [InlineData("test:network/child")]
    public async Task InvalidOrHierarchicalCenterScopeRejected(string center)
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        await Assert.ThrowsAsync<ArgumentException>(() => Capture(queue, center));
    }

    [Fact]
    public async Task MissingSnapshotOrBlankTokenNeverFallsBackToLiveQueue()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Page(queue, center, Guid.Empty));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Page(queue, center, Guid.NewGuid()));
        Assert.Equal("TEST_FROZEN_REVIEW_NOT_FOUND", ex.Message);
    }

    [Fact]
    public async Task SnapshotCaptureRoleCannotWriteOrDeleteAnyAcceptedMedicalEvidence()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a");
        Assert.Single((await Page(queue, center, await Capture(queue, center))).Items);
        await using var cn = await reader.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach (var sql in new[] {
            "DELETE FROM internal_test_review_event WHERE center_ref=@center",
            "UPDATE internal_test_review_work_item SET opaque_evidence_ref='test:forged' WHERE center_ref=@center",
            "DELETE FROM internal_test_frozen_review_snapshot WHERE center_ref=@center"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("center", center);
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", error.SqlState);
        }
    }

    [Fact]
    public async Task SchemaOwnerCannotNormallyRewriteExistingFrozenSnapshot()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        var id = await Capture(queue, center);
        await using var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand("""
            UPDATE internal_test_frozen_review_snapshot SET item_count=1
            WHERE snapshot_id=@id
            """, cn);
        cmd.Parameters.AddWithValue("id", id);
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("TEST_FROZEN_REVIEW_APPEND_ONLY", error.MessageText);
    }

    [Fact]
    public async Task FailedCaptureFromCorruptedPairedAuditDoesNotPersistSnapshot()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a");
        await using (var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var sql = new NpgsqlCommand("""
            UPDATE internal_test_review_case SET current_revision=0 WHERE center_ref=@center
            """, cn))
        {
            sql.Parameters.AddWithValue("center", center);
            await sql.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Capture(queue, center));
        await using var verify = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM internal_test_frozen_review_snapshot WHERE center_ref=@center
            """, verify);
        count.Parameters.AddWithValue("center", center);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task ASecondCaptureAfterNewCommitsCreatesDifferentManifest()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a");
        var first = await Capture(queue, center);
        await SeedAsync(journal, center, "test:doctor-b");
        var newer = await Capture(queue, center);
        Assert.NotEqual(first, newer);
        Assert.Single((await Page(queue, center, first)).Items);
        Assert.Equal(2, (await Page(queue, center, newer)).Items.Count);
    }

    [Fact]
    public async Task CapturedMemberRemainsVisibleAfterNewRevisionOfSameCase()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a");
        var id = await Capture(queue, center);
        await SeedAsync(journal, center, "test:doctor-a",
            EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianTaranomServiceContractClaim, 1);
        var old = await Page(queue, center, id);
        Assert.Single(old.Items);
        var updated = await Page(queue, center, await Capture(queue, center));
        Assert.Equal(2, updated.Items.Count);
    }

    [Fact]
    public async Task FrozenPageContainsNoVerifiedProviderOrPrivilegeFields()
    {
        var members = typeof(FrozenReviewPage).GetProperties().Select(p => p.Name).ToArray();
        foreach (var name in new[] { "Approved", "Certified", "Payee", "Eligible",
            "ReviewerGrant", "Wallet", "Authorization", "MedicalRecord" })
            Assert.DoesNotContain(name, members);
    }
}
