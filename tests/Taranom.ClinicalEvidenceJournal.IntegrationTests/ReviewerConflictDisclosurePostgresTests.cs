using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class ReviewerConflictDisclosurePostgresTests
{
    private const string Doctor = "test:doctor-a";
    private const string Reporter = "test:unverified-reporter";
    private const string Reviewer = "test:claimed-reviewer";
    private const string Link = "test:possible-relationship-evidence";
    private static string Pg => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG is required");
    private static string Center(string tag = "center") =>
        "test:" + tag + "-" + Guid.NewGuid().ToString("N");

    private static async Task<(NpgsqlDataSource Owner,
        PgClinicalEvidenceJournal Claims, NpgsqlDataSource Capturer,
        PgFrozenUnverifiedReviewQueue Frozen, PgHumanReviewNoteJournal Notes,
        PgReviewerConflictDisclosureJournal Conflicts)> ConnectAsync()
    {
        await ReviewJournalTestSchema.EnsureAsync()
            .WaitAsync(TestContext.Current.CancellationToken);
        var owner = NpgsqlDataSource.Create(Pg);
        var builder = new NpgsqlConnectionStringBuilder(Pg) {
            Username = "taranom_sprint12_snapshot_capture",
            Password = "ephemeral_synthetic_snapshot_capture"
        };
        var capturer = NpgsqlDataSource.Create(builder.ConnectionString);
        var claims = new PgClinicalEvidenceJournal(owner);
        var frozen = new PgFrozenUnverifiedReviewQueue(capturer);
        var notes = new PgHumanReviewNoteJournal(owner, frozen);
        var conflicts = new PgReviewerConflictDisclosureJournal(owner, notes);
        return (owner, claims, capturer, frozen, notes, conflicts);
    }

    private static async Task<ConflictNoticeCommand> SeedAsync(
        PgClinicalEvidenceJournal claims, PgFrozenUnverifiedReviewQueue frozen,
        PgHumanReviewNoteJournal notes, string center, string subject = Doctor)
    {
        var claim = new EvidenceSubmission(Guid.NewGuid(), 0, center, subject,
            EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim,
            "test:original-submitter", "test:unverified-qualification-ref");
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated,
            (await claims.SubmitAsync(center, subject, EvidenceSubjectKind.Physician, claim,
                TestContext.Current.CancellationToken)).Outcome);
        var snapshot = await frozen.CaptureAsync(center, null,
            TestContext.Current.CancellationToken);
        var note = new HumanReviewNoteCommand(Guid.NewGuid(), 0, center, subject,
            EvidenceSubjectKind.Physician, 1, snapshot, 0, null, Reviewer,
            "test:pending-verification-rationale");
        Assert.Equal(HumanReviewNoteOutcome.PendingSourceVerificationRecorded,
            (await notes.RecordAsync(note, TestContext.Current.CancellationToken)).Outcome);
        return new ConflictNoticeCommand(Guid.NewGuid(), 0, center, subject,
            EvidenceSubjectKind.Physician, 1, note.RequestId, Reporter, Link);
    }

    private static Task<ConflictNoticeOutcome> RecordAsync(
        PgReviewerConflictDisclosureJournal store, ConflictNoticeCommand command) =>
        store.RecordAsync(command, TestContext.Current.CancellationToken);

    private static Task<ConflictNoticeReadModel> ReadAsync(
        PgReviewerConflictDisclosureJournal store, ConflictNoticeCommand command) =>
        store.ReadAsync(command.CenterRef, command.SubjectRef,
            command.SubjectKind, command.EvidenceRevision,
            command.ReviewNoteRequestId, TestContext.Current.CancellationToken);

    private static async Task<long> CountAsync(NpgsqlDataSource db, string name,
        string center)
    {
        if (name is not ("internal_test_conflict_notice_head" or
            "internal_test_conflict_notice_event" or
            "internal_test_conflict_notice_audit"))
            throw new ArgumentException("Not an allowlisted test table", nameof(name));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {name} WHERE center_ref=@center", cn);
        cmd.Parameters.AddWithValue("center", center);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task NoDisclosureMeansUnknownNotConflictFree()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var command = await SeedAsync(claims, frozen, notes, Center());
        var state = await ReadAsync(conflicts, command);
        Assert.Equal(ConflictScreeningStatus.UnknownWithoutDisclosureEvidence,
            state.ScreeningStatus);
        Assert.Empty(state.Disclosures);
        Assert.Equal(0L, state.Revision);
    }

    [Fact]
    public async Task PotentialRelationshipIsRecordedAsPendingIndependentReview()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        Assert.Equal(ConflictNoticeOutcome.PotentialRelationshipRecorded,
            await RecordAsync(conflicts, cmd));
        var read = await ReadAsync(conflicts, cmd);
        Assert.Equal(ConflictScreeningStatus.PotentialRelationshipRequiresIndependentReview,
            read.ScreeningStatus);
        var notice = Assert.Single(read.Disclosures);
        Assert.Equal(cmd.RequestId, notice.RequestId);
        Assert.Equal(Reporter, notice.ReporterClaimRef);
        Assert.Equal(Link, notice.RelationshipEvidenceRef);
        Assert.Equal(1L, read.Revision);
        Assert.Equal(1L, await CountAsync(owner, "internal_test_conflict_notice_event", cmd.CenterRef));
        Assert.Equal(1L, await CountAsync(owner, "internal_test_conflict_notice_audit", cmd.CenterRef));
    }

    [Fact]
    public async Task TwoDisclosuresAreAppendOnlyWithSeparateRationales()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        await RecordAsync(conflicts, cmd);
        var later = cmd with {RequestId=Guid.NewGuid(), ExpectedRevision=1,
            RelationshipEvidenceRef="test:additional-possible-link"};
        Assert.Equal(ConflictNoticeOutcome.PotentialRelationshipRecorded,
            await RecordAsync(conflicts, later));
        var model = await ReadAsync(conflicts, cmd);
        Assert.Equal(2, model.Disclosures.Count);
        Assert.Equal(new long[]{1,2},
            model.Disclosures.Select(x=>x.Revision).ToArray());
        Assert.Equal(Link, model.Disclosures[0].RelationshipEvidenceRef);
        Assert.Equal("test:additional-possible-link",
            model.Disclosures[1].RelationshipEvidenceRef);
        Assert.Equal(2L, await CountAsync(owner, "internal_test_conflict_notice_audit", cmd.CenterRef));
    }

    [Fact]
    public async Task ExactHistoricalRequestReplayIsIdempotent()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        await RecordAsync(conflicts, cmd);
        await RecordAsync(conflicts, cmd with {RequestId=Guid.NewGuid(),
            ExpectedRevision=1});
        Assert.Equal(ConflictNoticeOutcome.Replayed, await RecordAsync(conflicts, cmd));
        Assert.Equal(2L, await CountAsync(owner, "internal_test_conflict_notice_event",cmd.CenterRef));
        Assert.Equal(2L, await CountAsync(owner, "internal_test_conflict_notice_audit",cmd.CenterRef));
    }

    [Fact]
    public async Task ReplayWithDifferentEvidenceConflicts()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        await RecordAsync(conflicts, cmd);
        Assert.Equal(ConflictNoticeOutcome.ConflictingReplay,
            await RecordAsync(conflicts, cmd with {
                RelationshipEvidenceRef="test:changed-reference"}));
        Assert.Single((await ReadAsync(conflicts, cmd)).Disclosures);
    }

    [Fact]
    public async Task ReplayWithDifferentReporterConflicts()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        await RecordAsync(conflicts, cmd);
        Assert.Equal(ConflictNoticeOutcome.ConflictingReplay,
            await RecordAsync(conflicts, cmd with {ReporterClaimRef="test:other"}));
    }

    [Fact]
    public async Task StaleRevisionDoesNotOverwriteReviewDisclosure()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        await RecordAsync(conflicts, cmd);
        Assert.Equal(ConflictNoticeOutcome.StaleRevision,
            await RecordAsync(conflicts, cmd with {RequestId=Guid.NewGuid()}));
        Assert.Single((await ReadAsync(conflicts, cmd)).Disclosures);
    }

    [Fact]
    public async Task DistinctConcurrentReportsOnSameRevisionOnlyCommitOnce()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        var other = cmd with {RequestId=Guid.NewGuid(),
            RelationshipEvidenceRef="test:other-link"};
        var answers = await Task.WhenAll(RecordAsync(conflicts,cmd),
            RecordAsync(conflicts,other));
        Assert.Single(answers,x=>x==ConflictNoticeOutcome.PotentialRelationshipRecorded);
        Assert.Single(answers,x=>x==ConflictNoticeOutcome.StaleRevision);
        Assert.Single((await ReadAsync(conflicts,cmd)).Disclosures);
    }

    [Fact]
    public async Task ConcurrentIdenticalReportsCreateOneAudit()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        var answers = await Task.WhenAll(RecordAsync(conflicts,cmd),
            RecordAsync(conflicts,cmd));
        Assert.Single(answers,x=>x==ConflictNoticeOutcome.PotentialRelationshipRecorded);
        Assert.Single(answers,x=>x==ConflictNoticeOutcome.Replayed);
        Assert.Equal(1L,await CountAsync(owner,"internal_test_conflict_notice_audit",cmd.CenterRef));
    }

    [Fact]
    public async Task WrongCenterCannotAttachDisclosureToAnotherReviewNote()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        Assert.Equal(ConflictNoticeOutcome.ReviewNoteMissingOrCorrupt,
            await RecordAsync(conflicts,cmd with {CenterRef=Center()}));
        Assert.Equal(0L,await CountAsync(owner,"internal_test_conflict_notice_event",cmd.CenterRef));
    }

    [Fact]
    public async Task AnotherSubjectCannotBorrowReviewNote()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        Assert.Equal(ConflictNoticeOutcome.ReviewNoteMissingOrCorrupt,
            await RecordAsync(conflicts,cmd with {SubjectRef="test:doctor-b"}));
    }

    [Fact]
    public async Task AnotherReviewNoteIdDoesNotMatchEvidenceLineage()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        Assert.Equal(ConflictNoticeOutcome.ReviewNoteMissingOrCorrupt,
            await RecordAsync(conflicts,cmd with {ReviewNoteRequestId=Guid.NewGuid()}));
    }

    [Theory]
    [InlineData("real:professional-ref")]
    [InlineData("")]
    [InlineData("test:")]
    [InlineData("test:relation/partner")]
    [InlineData("test:space injected")]
    public async Task RejectRealUnstructuredOrMalformedPotentialRelationshipRefs(string value)
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        Assert.Equal(ConflictNoticeOutcome.Invalid,
            await RecordAsync(conflicts,cmd with {RelationshipEvidenceRef=value}));
        Assert.Equal(0L,await CountAsync(owner,"internal_test_conflict_notice_head",cmd.CenterRef));
    }

    [Fact]
    public async Task UntrustedReporterMustStillHaveSyntacticallySyntheticReference()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        Assert.Equal(ConflictNoticeOutcome.Invalid,
            await RecordAsync(conflicts,cmd with {ReporterClaimRef="national-id:123"}));
    }

    [Fact]
    public async Task InvalidSubmissionIdCannotCreateConflictRow()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        Assert.Equal(ConflictNoticeOutcome.Invalid,
            await RecordAsync(conflicts,cmd with {RequestId=Guid.Empty}));
    }

    [Fact]
    public async Task InvalidSubjectKindCannotCreateConflictRow()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        Assert.Equal(ConflictNoticeOutcome.Invalid,
            await RecordAsync(conflicts,cmd with {SubjectKind=(EvidenceSubjectKind)99}));
    }

    [Fact]
    public async Task ReviewNoteScopeIsIndependentAcrossTwoOriginalNotes()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        var frozenPage = await frozen.CaptureAsync(cmd.CenterRef, null,TestContext.Current.CancellationToken);
        var nextNote = new HumanReviewNoteCommand(Guid.NewGuid(),1,cmd.CenterRef,
            cmd.SubjectRef,cmd.SubjectKind,cmd.EvidenceRevision,frozenPage,0,null,
            "test:another-reviewer","test:another-rationale");
        Assert.Equal(HumanReviewNoteOutcome.PendingSourceVerificationRecorded,
            (await notes.RecordAsync(nextNote,TestContext.Current.CancellationToken)).Outcome);
        await RecordAsync(conflicts,cmd);
        var other = cmd with {RequestId=Guid.NewGuid(),ReviewNoteRequestId=nextNote.RequestId};
        Assert.Equal(ConflictScreeningStatus.UnknownWithoutDisclosureEvidence,
            (await ReadAsync(conflicts,other)).ScreeningStatus);
        Assert.Equal(ConflictNoticeOutcome.PotentialRelationshipRecorded,
            await RecordAsync(conflicts,other));
        Assert.Single((await ReadAsync(conflicts,cmd)).Disclosures);
        Assert.Single((await ReadAsync(conflicts,other)).Disclosures);
    }

    [Fact]
    public async Task FailedAuditRollsBackNoteAndHeadTogether()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center("abort-coi-audit"));
        var error=await Assert.ThrowsAsync<PostgresException>(()=>RecordAsync(conflicts,cmd));
        Assert.Equal("TEST_CONFLICT_NOTICE_AUDIT_UNAVAILABLE",error.MessageText);
        Assert.Equal(0L,await CountAsync(owner,"internal_test_conflict_notice_head",cmd.CenterRef));
        Assert.Equal(0L,await CountAsync(owner,"internal_test_conflict_notice_event",cmd.CenterRef));
        Assert.Equal(0L,await CountAsync(owner,"internal_test_conflict_notice_audit",cmd.CenterRef));
    }

    [Fact]
    public async Task AcceptedDisclosureAndAuditCannotBeUpdatedOrDeleted()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        await RecordAsync(conflicts,cmd);
        await using var cn=await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach(var name in new []{"internal_test_conflict_notice_event",
            "internal_test_conflict_notice_audit"})
        {
            await using var sql = new NpgsqlCommand(
                $"DELETE FROM {name} WHERE center_ref=@center",cn);
            sql.Parameters.AddWithValue("center",cmd.CenterRef);
            var error=await Assert.ThrowsAsync<PostgresException>(async()=>
                await sql.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("TEST_CONFLICT_NOTICE_APPEND_ONLY",error.MessageText);
        }
    }

    [Fact]
    public async Task TamperedCurrentRevisionFailsClosedOnReadAndWrite()
    {
        var (owner, claims, capturer, frozen, notes, conflicts) = await ConnectAsync();
        await using var _ = owner;
        await using var __ = capturer;
        var cmd = await SeedAsync(claims, frozen, notes, Center());
        await RecordAsync(conflicts,cmd);
        await using(var cn=await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using(var sql=new NpgsqlCommand("""
            UPDATE internal_test_conflict_notice_head SET current_revision=0
            WHERE center_ref=@center
            """,cn))
        {
            sql.Parameters.AddWithValue("center",cmd.CenterRef);
            await sql.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var read=await Assert.ThrowsAsync<InvalidOperationException>(()=>ReadAsync(conflicts,cmd));
        Assert.Equal("TEST_CONFLICT_NOTICE_HISTORY_CORRUPT",read.Message);
        var again=await Assert.ThrowsAsync<InvalidOperationException>(()=>
            RecordAsync(conflicts,cmd with {RequestId=Guid.NewGuid(),ExpectedRevision=1}));
        Assert.Equal("TEST_CONFLICT_NOTICE_HISTORY_CORRUPT",again.Message);
    }

    [Fact]
    public void NoConflictFreeOrMedicalApprovalVocabularyExists()
    {
        Assert.DoesNotContain(Enum.GetNames<ConflictScreeningStatus>(),
            name=>name is "NoConflict" or "Cleared" or "Authorized" or "Approved");
        Assert.DoesNotContain(Enum.GetNames<ConflictNoticeOutcome>(),
            name=>name is "Activated" or "Eligible" or "PhysicianApproved");
        Assert.DoesNotContain(typeof(ConflictNoticeReadModel).GetProperties(),
            p=>p.Name is "Approval" or "Permission" or "ReviewerAttestation");
    }
}
