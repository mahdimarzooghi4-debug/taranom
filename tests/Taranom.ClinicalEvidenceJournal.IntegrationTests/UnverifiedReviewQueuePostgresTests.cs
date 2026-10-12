using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

// GENUINE disposable CI PostgreSQL; synthetic cases only, never real users.
public sealed class UnverifiedReviewQueuePostgresTests
{
    private static readonly Lazy<Task> Ready = new(() => ReviewJournalTestSchema.EnsureAsync());
    private const string ReaderUser = "taranom_sprint11_review_reader";
    private const string ReaderPassword = "ephemeral_synthetic_review_only";
    private const string Manager = "test:manager";
    private const string Claimant = "test:claimant";
    private const string Proof = "test:unverified-document-ref";

    private static string Pg => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG must point at isolated synthetic CI database");

    private static string NewCenter() =>
        "test:center-" + Guid.NewGuid().ToString("N");

    private static async Task<(NpgsqlDataSource Owner, PgClinicalEvidenceJournal Journal,
        NpgsqlDataSource Reader, PgUnverifiedEvidenceReviewQueue Queue)> ConnectAsync()
    {
        await Ready.Value.WaitAsync(TestContext.Current.CancellationToken);
        var owner = NpgsqlDataSource.Create(Pg);
        var readerConfig = new NpgsqlConnectionStringBuilder(Pg)
        {
            Username = ReaderUser,
            Password = ReaderPassword
        };
        var reader = NpgsqlDataSource.Create(readerConfig.ConnectionString);
        return (owner, new PgClinicalEvidenceJournal(owner),
            reader, new PgUnverifiedEvidenceReviewQueue(reader));
    }

    private static async Task SeedAsync(PgClinicalEvidenceJournal journal,
        string center, string subject, EvidenceSubjectKind subjectKind,
        EvidenceKind evidenceKind, long revision = 0)
    {
        var item = new EvidenceSubmission(Guid.NewGuid(), revision, center, subject,
            subjectKind, evidenceKind, Claimant, Proof);
        var result = await journal.SubmitAsync(center, subject, subjectKind,
            item, TestContext.Current.CancellationToken);
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated, result.Outcome);
    }

    private static Task<UnverifiedReviewQueuePage> ListAsync(
        PgUnverifiedEvidenceReviewQueue queue, string center, int pageSize = 20,
        EvidenceSubjectKind? kind = null, ReviewQueueCursor? cursor = null) =>
        queue.ListAsync(center, kind, pageSize, cursor, TestContext.Current.CancellationToken);

    [Fact]
    public async Task NoCasesReturnsEmptyPageNotZeroDoctorsOrVerifiedStatus()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var result = await ListAsync(queue, NewCenter());
        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task EachCenterSeesOnlyItsOwnSubmittedReviewItems()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var first = NewCenter();
        var second = NewCenter();
        await SeedAsync(journal, first, "test:doctor-a", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        await SeedAsync(journal, second, "test:doctor-a", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianTaranomServiceContractClaim);
        var one = Assert.Single((await ListAsync(queue, first)).Items);
        var two = Assert.Single((await ListAsync(queue, second)).Items);
        Assert.Equal(first, one.CenterRef);
        Assert.Equal(second, two.CenterRef);
        Assert.NotEqual(one.EvidenceKind, two.EvidenceKind);
    }

    [Fact]
    public async Task ACenterCanListUnverifiedManagerAndPhysicianClaimsSeparately()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, Manager, EvidenceSubjectKind.CenterRepresentative,
            EvidenceKind.CenterManagerAuthorityClaim);
        await SeedAsync(journal, center, "test:doctor-b", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        var all = await ListAsync(queue, center);
        Assert.Equal(2, all.Items.Count);
        var rep = Assert.Single((await ListAsync(queue, center,
            kind: EvidenceSubjectKind.CenterRepresentative)).Items);
        var doctor = Assert.Single((await ListAsync(queue, center,
            kind: EvidenceSubjectKind.Physician)).Items);
        Assert.Equal(EvidenceKind.CenterManagerAuthorityClaim, rep.EvidenceKind);
        Assert.Equal(EvidenceKind.PhysicianProfessionalQualificationClaim, doctor.EvidenceKind);
    }

    [Fact]
    public async Task ProfessionalAndProviderContractClaimsRemainDistinct()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        const string subject = "test:doctor-a";
        await SeedAsync(journal, center, subject, EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        await SeedAsync(journal, center, subject, EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianTaranomServiceContractClaim, 1);
        var rows = (await ListAsync(queue, center)).Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal(new long[] { 1, 2 }, rows.Select(x => x.Revision).ToArray());
        Assert.Equal(EvidenceKind.PhysicianProfessionalQualificationClaim, rows[0].EvidenceKind);
        Assert.Equal(EvidenceKind.PhysicianTaranomServiceContractClaim, rows[1].EvidenceKind);
    }

    [Fact]
    public async Task CursorPaginatesOneCenterWithoutReplayingAnItem()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        foreach (var subject in new[] { "test:doctor-c", "test:doctor-a", "test:doctor-b" })
            await SeedAsync(journal, center, subject, EvidenceSubjectKind.Physician,
                EvidenceKind.PhysicianProfessionalQualificationClaim);
        var first = await ListAsync(queue, center, pageSize: 1);
        Assert.Equal("test:doctor-a", Assert.Single(first.Items).SubjectRef);
        Assert.NotNull(first.NextCursor);
        var second = await ListAsync(queue, center, pageSize: 1, cursor: first.NextCursor);
        Assert.Equal("test:doctor-b", Assert.Single(second.Items).SubjectRef);
        Assert.NotNull(second.NextCursor);
        var last = await ListAsync(queue, center, pageSize: 1, cursor: second.NextCursor);
        Assert.Equal("test:doctor-c", Assert.Single(last.Items).SubjectRef);
        Assert.Null(last.NextCursor);
    }

    [Fact]
    public async Task CursorAfterFinalPageReturnsEmptyNoUnsafeFallback()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        var initial = await ListAsync(queue, center);
        Assert.Null(initial.NextCursor);
        var cursor = new ReviewQueueCursor(center, null, "test:doctor-a",
            EvidenceSubjectKind.Physician, 1);
        var remaining = await ListAsync(queue, center, cursor: cursor);
        Assert.Empty(remaining.Items);
    }

    [Fact]
    public async Task APageCursorCannotBeReusedForAnotherCenterOrSubjectFilter()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        var cursor = new ReviewQueueCursor(center, EvidenceSubjectKind.Physician,
            "test:doctor-a", EvidenceSubjectKind.Physician, 1);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ListAsync(queue, NewCenter(), kind: EvidenceSubjectKind.Physician, cursor: cursor));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ListAsync(queue, center, cursor: cursor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task InvalidTechnicalPageSizeIsRejected(int size)
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ListAsync(queue, NewCenter(), pageSize: size));
    }

    [Theory]
    [InlineData("real:hospital")]
    [InlineData("test:")]
    [InlineData("test:parent/child")]
    [InlineData("")]
    public async Task RealOrHierarchicalCenterIdentifiersAreRejected(string center)
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        await Assert.ThrowsAsync<ArgumentException>(() => ListAsync(queue, center));
    }

    [Fact]
    public async Task InvalidCursorAndUnknownKindFailBeforeDatabaseRead()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ListAsync(queue, center, kind: (EvidenceSubjectKind)99));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ListAsync(queue, center, cursor: new ReviewQueueCursor(
                center, null, "national-id:doctor", EvidenceSubjectKind.Physician, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ListAsync(queue, center, cursor: new ReviewQueueCursor(
                center, null, "test:doctor-a", EvidenceSubjectKind.Physician, 0)));
    }

    [Fact]
    public async Task ReadOnlyDatabaseRoleCanReadButCannotMutateEvents()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        Assert.Single((await ListAsync(queue, center)).Items);
        await using var cn = await reader.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM internal_test_review_event WHERE center_ref=@center
            """, cn);
        cmd.Parameters.AddWithValue("center", center);
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("42501", error.SqlState);
        Assert.Single((await ListAsync(queue, center)).Items);
    }

    [Fact]
    public async Task TamperedRevisionBlocksQueueResultsInsteadOfSilentlyShowingThem()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-a", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        await using (var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var sql = new NpgsqlCommand("""
            UPDATE internal_test_review_case SET current_revision=0 WHERE center_ref=@center
            """, cn))
        {
            sql.Parameters.AddWithValue("center", center);
            await sql.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ListAsync(queue, center));
        Assert.Equal("TEST_REVIEW_QUEUE_INTEGRITY_FAILED", ex.Message);
    }

    [Fact]
    public void NoReviewDecisionOrPrivilegedIdentityAppearsInQueueDto()
    {
        var fields = typeof(UnverifiedReviewQueueItem).GetProperties().Select(p => p.Name).ToArray();
        foreach (var name in new[] { "Approved", "Verified", "License", "AccessGrant",
                 "ReviewerDecision", "MedicalRecord", "Wallet", "ClinicalPermission" })
            Assert.DoesNotContain(name, fields);
        Assert.DoesNotContain(typeof(UnverifiedReviewQueuePage).GetProperties(),
            p => p.Name == "ReviewerAuthorization");
    }

    [Fact]
    public async Task NewRowsAfterCursorMayAppearAndNoSnapshotStabilityIsClaimed()
    {
        var (owner, journal, reader, queue) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = reader;
        var center = NewCenter();
        await SeedAsync(journal, center, "test:doctor-b", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        await SeedAsync(journal, center, "test:doctor-c", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        var first = await ListAsync(queue, center, pageSize: 1);
        Assert.NotNull(first.NextCursor);
        await SeedAsync(journal, center, "test:doctor-z", EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim);
        var second = await ListAsync(queue, center, pageSize: 10, cursor: first.NextCursor);
        Assert.Equal(new[] { "test:doctor-c", "test:doctor-z" },
            second.Items.Select(i => i.SubjectRef).ToArray());
        Assert.Null(second.NextCursor);
    }
}
