using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class ConflictPacketPostgresTests
{
    private static string Pg => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("Isolated PostgreSQL required");

    private static async Task<(NpgsqlDataSource Owner, NpgsqlDataSource SnapshotDb,
        PgReviewerConflictDisclosureJournal Notices, PgUnverifiedConflictPacketJournal Packets,
        ConflictNoticeCommand Notice, ConflictPacketCommand Packet)> SetupAsync(string prefix = "center")
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var owner = NpgsqlDataSource.Create(Pg);
        var settings = new NpgsqlConnectionStringBuilder(Pg) {
            Username = "taranom_sprint12_snapshot_capture",
            Password = "ephemeral_synthetic_snapshot_capture"
        };
        var reader = NpgsqlDataSource.Create(settings.ConnectionString);
        var claims = new PgClinicalEvidenceJournal(owner);
        var frozen = new PgFrozenUnverifiedReviewQueue(reader);
        var notes = new PgHumanReviewNoteJournal(owner, frozen);
        var notices = new PgReviewerConflictDisclosureJournal(owner, notes);
        var packets = new PgUnverifiedConflictPacketJournal(owner, notices);
        var center = "test:" + prefix + "-" + Guid.NewGuid().ToString("N");
        const string subject = "test:doctor";
        var claim = new EvidenceSubmission(Guid.NewGuid(), 0, center, subject,
            EvidenceSubjectKind.Physician, EvidenceKind.PhysicianProfessionalQualificationClaim,
            "test:claimant", "test:unverified-medical-evidence");
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated,
            (await claims.SubmitAsync(center, subject, EvidenceSubjectKind.Physician,
                claim, TestContext.Current.CancellationToken)).Outcome);
        var snapshot = await frozen.CaptureAsync(center, null, TestContext.Current.CancellationToken);
        var note = new HumanReviewNoteCommand(Guid.NewGuid(), 0, center, subject,
            EvidenceSubjectKind.Physician, 1, snapshot, 0, null,
            "test:reviewer-claim", "test:unverified-rationale");
        Assert.Equal(HumanReviewNoteOutcome.PendingSourceVerificationRecorded,
            (await notes.RecordAsync(note, TestContext.Current.CancellationToken)).Outcome);
        var notice = new ConflictNoticeCommand(Guid.NewGuid(), 0, center, subject,
            EvidenceSubjectKind.Physician, 1, note.RequestId,
            "test:reporter-claim", "test:potential-relationship");
        var packet = new ConflictPacketCommand(Guid.NewGuid(), center, subject,
            EvidenceSubjectKind.Physician, 1, note.RequestId, 1, "test:initiator-claim");
        return (owner, reader, notices, packets, notice, packet);
    }

    private static Task<ConflictPacketOutcome> Prepare(
        PgUnverifiedConflictPacketJournal packets, ConflictPacketCommand packet) =>
        packets.PrepareAsync(packet, TestContext.Current.CancellationToken);

    private static async Task<Guid> FindId(NpgsqlDataSource db, Guid request)
    {
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT packet_id FROM internal_test_conflict_packet WHERE request_ref=@request", cn);
        cmd.Parameters.AddWithValue("request", request);
        return (Guid)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Packet not found"));
    }

    [Fact]
    public async Task NoDisclosureCannotProducePacket()
    {
        var (db, reader, notices, packets, notice, packet) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        Assert.Equal(ConflictPacketOutcome.NoPotentialDisclosure, await Prepare(packets, packet));
    }

    [Fact]
    public async Task PendingNoticeCreatesOnlyUnverifiedPacketAndPairedAudit()
    {
        var (db, reader, notices, packets, notice, packet) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        Assert.Equal(ConflictNoticeOutcome.PotentialRelationshipRecorded,
            await notices.RecordAsync(notice, TestContext.Current.CancellationToken));
        Assert.Equal(ConflictPacketOutcome.UnverifiedPacketPrepared, await Prepare(packets, packet));
        var saved = await packets.ReadAsync(await FindId(db, packet.RequestId),
            TestContext.Current.CancellationToken);
        Assert.Equal(packet, saved.Command);
        Assert.Equal(notice.RequestId, Assert.Single(saved.CapturedDisclosures).RequestId);
        Assert.Equal(ConflictPacketOutcome.ExactReplay, await Prepare(packets, packet));
        Assert.Equal(ConflictPacketOutcome.ConflictingReplay,
            await Prepare(packets, packet with { InitiatorClaimRef = "test:changed" }));
        Assert.Equal(ConflictPacketOutcome.AlreadyPreparedForRevision,
            await Prepare(packets, packet with { RequestId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task LaterNoticeRequiresNewVersionAndPreservesOlderManifest()
    {
        var (db, reader, notices, packets, notice, packet) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        await notices.RecordAsync(notice, TestContext.Current.CancellationToken);
        await notices.RecordAsync(notice with { RequestId = Guid.NewGuid(),
            ExpectedRevision = 1, RelationshipEvidenceRef = "test:another-relationship" },
            TestContext.Current.CancellationToken);
        Assert.Equal(ConflictPacketOutcome.SourceRevisionChanged, await Prepare(packets, packet));
        var newer = packet with { RequestId = Guid.NewGuid(), ExpectedDisclosureRevision = 2 };
        Assert.Equal(ConflictPacketOutcome.UnverifiedPacketPrepared, await Prepare(packets, newer));
        var stored = await packets.ReadAsync(await FindId(db, newer.RequestId),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, stored.CapturedDisclosures.Count);
    }

    [Fact]
    public async Task RaceOnSameRevisionIsSerialized()
    {
        var (db, reader, notices, packets, notice, packet) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        await notices.RecordAsync(notice, TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(
            Prepare(packets, packet), Prepare(packets, packet with { RequestId = Guid.NewGuid() }));
        Assert.Single(results, x => x == ConflictPacketOutcome.UnverifiedPacketPrepared);
        Assert.Single(results, x => x == ConflictPacketOutcome.AlreadyPreparedForRevision);
    }

    [Fact]
    public async Task AuditUnavailableRollsBackPacket()
    {
        var (db, reader, notices, packets, notice, packet) =
            await SetupAsync("abort-packet-audit");
        await using var _ = db; await using var __ = reader;
        await notices.RecordAsync(notice, TestContext.Current.CancellationToken);
        var err = await Assert.ThrowsAsync<PostgresException>(() => Prepare(packets, packet));
        Assert.Equal("TEST_CONFLICT_PACKET_AUDIT_UNAVAILABLE", err.MessageText);
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var count = new NpgsqlCommand(
            "SELECT COUNT(*) FROM internal_test_conflict_packet WHERE center_ref=@center", cn);
        count.Parameters.AddWithValue("center", packet.CenterRef);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public void ContractNeverExpressesReviewerApprovalOrAssignment()
    {
        Assert.DoesNotContain(Enum.GetNames<ConflictPacketOutcome>(),
            value => value is "Approved" or "Assigned" or "Cleared" or "Delivered");
    }
}
