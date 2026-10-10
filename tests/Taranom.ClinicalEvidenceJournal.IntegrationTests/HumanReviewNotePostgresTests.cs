using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class HumanReviewNotePostgresTests
{
    private const string Claimant = "test:unverified-claimant";
    private const string Doctor = "test:doctor-a";
    private const string Reviewer = "test:claimed-reviewer";
    private const string Rationale = "test:review-notes-not-verified";
    private static string Pg => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG required");

    private static string Center(string prefix = "center") =>
        "test:" + prefix + "-" + Guid.NewGuid().ToString("N");

    private static async Task<(NpgsqlDataSource Owner, PgClinicalEvidenceJournal Journal,
        NpgsqlDataSource Capture, PgFrozenUnverifiedReviewQueue Frozen,
        PgHumanReviewNoteJournal Reviews)> ConnectAsync()
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var owner = NpgsqlDataSource.Create(Pg);
        var settings = new NpgsqlConnectionStringBuilder(Pg) {
            Username = "taranom_sprint12_snapshot_capture",
            Password = "ephemeral_synthetic_snapshot_capture"
        };
        var capture = NpgsqlDataSource.Create(settings.ConnectionString);
        var frozen = new PgFrozenUnverifiedReviewQueue(capture);
        return (owner, new PgClinicalEvidenceJournal(owner), capture, frozen,
            new PgHumanReviewNoteJournal(owner, frozen));
    }

    private static async Task<(Guid Snapshot, HumanReviewNoteCommand Input)> SeedAsync(
        PgClinicalEvidenceJournal journal, PgFrozenUnverifiedReviewQueue frozen,
        string center, string subject = Doctor,
        EvidenceSubjectKind subjectKind = EvidenceSubjectKind.Physician,
        EvidenceKind evidenceKind = EvidenceKind.PhysicianProfessionalQualificationClaim)
    {
        var submission = new EvidenceSubmission(Guid.NewGuid(), 0, center, subject,
            subjectKind, evidenceKind, Claimant, "test:proof-only");
        var outcome = await journal.SubmitAsync(center, subject, subjectKind,
            submission, TestContext.Current.CancellationToken);
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated, outcome.Outcome);
        var snapshot = await frozen.CaptureAsync(center, null, TestContext.Current.CancellationToken);
        var command = new HumanReviewNoteCommand(Guid.NewGuid(), 0, center, subject,
            subjectKind, 1, snapshot, 0, null, Reviewer, Rationale);
        return (snapshot, command);
    }

    private static Task<HumanReviewNoteResult> Record(PgHumanReviewNoteJournal journal,
        HumanReviewNoteCommand command) =>
        journal.RecordAsync(command, TestContext.Current.CancellationToken);

    private static Task<IReadOnlyList<HumanReviewNoteRecord>> Read(
        PgHumanReviewNoteJournal journal, HumanReviewNoteCommand command) =>
        journal.ReadAsync(command.CenterRef, command.SubjectRef, command.SubjectKind,
            command.EvidenceRevision, TestContext.Current.CancellationToken);

    private static async Task<long> Count(NpgsqlDataSource owner, string table,
        string center)
    {
        if (table is not ("internal_test_human_note_head" or
            "internal_test_human_note" or "internal_test_human_note_audit"))
            throw new ArgumentException("Unknown synthetic table", nameof(table));
        await using var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE center_ref=@center", cn);
        cmd.Parameters.AddWithValue("center", center);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task AClaimedReviewerCanOnlyRecordPendingSourceVerification()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, command) = await SeedAsync(journal, frozen, center);
        var written = await Record(notes, command);
        Assert.Equal(HumanReviewNoteOutcome.PendingSourceVerificationRecorded, written.Outcome);
        Assert.Equal(1L, written.CurrentRevision);
        var record = Assert.Single(await Read(notes, command));
        Assert.Equal(Reviewer, record.ReviewerClaimRef);
        Assert.Equal(Rationale, record.RationaleEvidenceRef);
        Assert.Equal(command.FrozenSnapshotId, record.FrozenSnapshotId);
        Assert.Equal(1L, await Count(owner, "internal_test_human_note", center));
        Assert.Equal(1L, await Count(owner, "internal_test_human_note_audit", center));
    }

    [Fact]
    public async Task ExactReplayDoesNotCreateAnotherNote()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, command) = await SeedAsync(journal, frozen, center);
        await Record(notes, command);
        var replay = await Record(notes, command);
        Assert.Equal(HumanReviewNoteOutcome.Replayed, replay.Outcome);
        Assert.Equal(1L, await Count(owner, "internal_test_human_note", center));
        Assert.Equal(1L, await Count(owner, "internal_test_human_note_audit", center));
    }

    [Fact]
    public async Task SameRequestIdDifferentRationaleMustConflict()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, command) = await SeedAsync(journal, frozen, center);
        await Record(notes, command);
        var second = await Record(notes, command with { RationaleEvidenceRef = "test:changed" });
        Assert.Equal(HumanReviewNoteOutcome.ConflictingReplay, second.Outcome);
        Assert.Single(await Read(notes, command));
    }

    [Fact]
    public async Task StaleNoteRevisionCannotOverwriteHistory()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, command) = await SeedAsync(journal, frozen, center);
        await Record(notes, command);
        var stale = command with { RequestId = Guid.NewGuid() };
        Assert.Equal(HumanReviewNoteOutcome.StaleRevision, (await Record(notes, stale)).Outcome);
        Assert.Single(await Read(notes, command));
    }

    [Fact]
    public async Task TwoDistinctNotesKeepLineageAndIndependentReviewRationale()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, first) = await SeedAsync(journal, frozen, center);
        await Record(notes, first);
        var second = first with { RequestId = Guid.NewGuid(),
            ExpectedNoteRevision = 1, RationaleEvidenceRef = "test:follow-up" };
        Assert.Equal(HumanReviewNoteOutcome.PendingSourceVerificationRecorded,
            (await Record(notes, second)).Outcome);
        var history = await Read(notes, first);
        Assert.Equal(2, history.Count);
        Assert.Equal(new long[] {1,2}, history.Select(x=>x.NoteRevision).ToArray());
        Assert.Equal(Rationale, history[0].RationaleEvidenceRef);
        Assert.Equal("test:follow-up", history[1].RationaleEvidenceRef);
        Assert.Equal(2L, await Count(owner, "internal_test_human_note_audit", center));
    }

    [Fact]
    public async Task ConcurrentWritersWithSameVersionCannotBothCommit()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, first) = await SeedAsync(journal, frozen, center);
        var other = first with { RequestId = Guid.NewGuid(), RationaleEvidenceRef = "test:other" };
        var outcomes = await Task.WhenAll(Record(notes, first), Record(notes, other));
        Assert.Single(outcomes, x => x.Outcome ==
            HumanReviewNoteOutcome.PendingSourceVerificationRecorded);
        Assert.Single(outcomes, x => x.Outcome == HumanReviewNoteOutcome.StaleRevision);
        Assert.Single(await Read(notes, first));
    }

    [Fact]
    public async Task ConcurrentExactRetryOnlyAddsOneNoteAndAudit()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, command) = await SeedAsync(journal, frozen, center);
        var outcomes = await Task.WhenAll(Record(notes, command), Record(notes, command));
        Assert.Single(outcomes, x => x.Outcome ==
            HumanReviewNoteOutcome.PendingSourceVerificationRecorded);
        Assert.Single(outcomes, x => x.Outcome == HumanReviewNoteOutcome.Replayed);
        Assert.Equal(1L, await Count(owner, "internal_test_human_note_audit", center));
    }

    [Fact]
    public async Task AnotherCentersSnapshotCannotBeUsedAsReviewEvidence()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var a = Center();
        var b = Center();
        var (_, cmd) = await SeedAsync(journal, frozen, a);
        var denied = await Record(notes, cmd with { CenterRef = b });
        Assert.Equal(HumanReviewNoteOutcome.ScopeDenied, denied.Outcome);
        Assert.Equal(0L, await Count(owner, "internal_test_human_note", a));
        Assert.Equal(0L, await Count(owner, "internal_test_human_note", b));
    }

    [Fact]
    public async Task DifferentEvidenceInSameSnapshotMustNotBeRelabeled()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, command) = await SeedAsync(journal, frozen, center);
        var denied = await Record(notes, command with { SubjectRef = "test:other-doctor" });
        Assert.Equal(HumanReviewNoteOutcome.ScopeDenied, denied.Outcome);
    }

    [Fact]
    public async Task MissingOrMismatchedFrozenSnapshotNeverFallsBackToLiveQueue()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, command) = await SeedAsync(journal, frozen, center);
        Assert.Equal(HumanReviewNoteOutcome.ScopeDenied,
            (await Record(notes, command with { FrozenSnapshotId = Guid.NewGuid() })).Outcome);
        Assert.Equal(HumanReviewNoteOutcome.Invalid,
            (await Record(notes, command with { FrozenSnapshotId = Guid.Empty })).Outcome);
    }

    [Fact]
    public async Task InvalidReviewIdentityOrRationaleDoesNotCreateAnything()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, cmd) = await SeedAsync(journal, frozen, center);
        var invalid = await Record(notes, cmd with { ReviewerClaimRef = "real:person-id" });
        Assert.Equal(HumanReviewNoteOutcome.Invalid, invalid.Outcome);
        Assert.Equal(HumanReviewNoteOutcome.Invalid,
            (await Record(notes, cmd with { RationaleEvidenceRef = "" })).Outcome);
        Assert.Equal(0L, await Count(owner, "internal_test_human_note_head", center));
    }

    [Fact]
    public async Task WrongSubjectCategoryOrEvidenceRevisionCannotCreateNote()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, cmd) = await SeedAsync(journal, frozen, center);
        Assert.Equal(HumanReviewNoteOutcome.ScopeDenied,
            (await Record(notes, cmd with { SubjectKind = EvidenceSubjectKind.CenterRepresentative })).Outcome);
        Assert.Equal(HumanReviewNoteOutcome.ScopeDenied,
            (await Record(notes, cmd with { EvidenceRevision = 2 })).Outcome);
    }

    [Fact]
    public async Task ManagerAuthorityClaimIsStillAnUnverifiedObservation()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, cmd) = await SeedAsync(journal, frozen, center, "test:representative",
            EvidenceSubjectKind.CenterRepresentative, EvidenceKind.CenterManagerAuthorityClaim);
        Assert.Equal(HumanReviewNoteOutcome.PendingSourceVerificationRecorded,
            (await Record(notes, cmd)).Outcome);
        Assert.Single(await Read(notes, cmd));
    }

    [Fact]
    public async Task AuditFailureRollsBackNoteAuditAndHeadAtomically()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center("abort-review-audit");
        var (_, cmd) = await SeedAsync(journal, frozen, center);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Record(notes, cmd));
        Assert.Equal("TEST_HUMAN_REVIEW_AUDIT_UNAVAILABLE", error.MessageText);
        Assert.Equal(0L, await Count(owner, "internal_test_human_note_head", center));
        Assert.Equal(0L, await Count(owner, "internal_test_human_note", center));
        Assert.Equal(0L, await Count(owner, "internal_test_human_note_audit", center));
    }

    [Fact]
    public async Task AppendOnlyEventsAndAuditCannotBeDeletedByOrdinaryOwnerDml()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, cmd) = await SeedAsync(journal, frozen, center);
        await Record(notes, cmd);
        await using var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach (var table in new[] {
            "internal_test_human_note","internal_test_human_note_audit"
        })
        {
            await using var command = new NpgsqlCommand(
                $"DELETE FROM {table} WHERE center_ref=@center", cn);
            command.Parameters.AddWithValue("center", center);
            var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("TEST_HUMAN_REVIEW_APPEND_ONLY", ex.MessageText);
        }
    }

    [Fact]
    public async Task CorruptHeadRevisionFailsClosedForReadsAndNewWrites()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, cmd) = await SeedAsync(journal, frozen, center);
        await Record(notes, cmd);
        await using (var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var sql = new NpgsqlCommand("""
            UPDATE internal_test_human_note_head SET current_note_revision=0
            WHERE center_ref=@center
            """, cn))
        {
            sql.Parameters.AddWithValue("center", center);
            await sql.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var read = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(notes, cmd));
        Assert.Equal("TEST_HUMAN_REVIEW_HISTORY_CORRUPT", read.Message);
        var again = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Record(notes, cmd with {RequestId=Guid.NewGuid(),ExpectedNoteRevision=1}));
        Assert.Equal("TEST_HUMAN_REVIEW_HISTORY_CORRUPT", again.Message);
    }

    [Fact]
    public async Task ExistingClaimWithNoNotesReturnsEmptyNotAnApprovedReview()
    {
        var (owner, journal, capture, frozen, notes) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capture;
        var center = Center();
        var (_, cmd) = await SeedAsync(journal, frozen, center);
        Assert.Empty(await Read(notes, cmd));
    }

    [Fact]
    public void ReviewNoteVocabCannotIssueQualificationOrMedicalPermission()
    {
        Assert.DoesNotContain(Enum.GetNames<HumanReviewNoteOutcome>(),
            name => name.Contains("Approved", StringComparison.Ordinal) ||
                    name.Contains("Activated", StringComparison.Ordinal) ||
                    name.Contains("Licensed", StringComparison.Ordinal) ||
                    name.Contains("Qualified", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(HumanReviewNoteRecord).GetProperties(),
            p => p.Name is "AccessGrant" or "ClinicalPermission" or "ContractVerified");
    }
}
