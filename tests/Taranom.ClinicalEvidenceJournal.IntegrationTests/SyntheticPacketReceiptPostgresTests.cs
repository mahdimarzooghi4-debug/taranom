using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class SyntheticPacketReceiptPostgresTests
{
    private static string Pg => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("Synthetic PostgreSQL only");

    private sealed class TestSource(TechnicalReceiptSourceEvidence? item)
        : ITechnicalReceiptAuthoritySource
    {
        internal bool Broken { get; set; }
        public Task<TechnicalReceiptSourceEvidence?> ResolveAsync(Guid packet, CancellationToken ct)
        {
            if (Broken) throw new InvalidOperationException("simulated source outage");
            return Task.FromResult(item);
        }
    }

    private static TechnicalReceiptSourceEvidence Attestation(
        TechnicalReceiptCommand command) => new(command.PacketId, command.RequestId, command.CenterRef,
            command.RecipientClaimRef, command.RecipientClaimRef,
            command.GrantVersionRef, command.ReceiptEvidenceRef,
            true, true, true, true);

    private static async Task<(NpgsqlDataSource Db, NpgsqlDataSource Reader,
        PgReviewerConflictDisclosureJournal Notices, PgUnverifiedConflictPacketJournal Packets,
        ConflictNoticeCommand Notice, TechnicalReceiptCommand Receipt)> SetupAsync(
        string prefix = "center")
    {
        await ReviewJournalTestSchema.EnsureAsync().WaitAsync(TestContext.Current.CancellationToken);
        var db = NpgsqlDataSource.Create(Pg);
        var config = new NpgsqlConnectionStringBuilder(Pg) {
            Username = "taranom_sprint12_snapshot_capture",
            Password = "ephemeral_synthetic_snapshot_capture"
        };
        var reader = NpgsqlDataSource.Create(config.ConnectionString);
        var evidence = new PgClinicalEvidenceJournal(db);
        var frozen = new PgFrozenUnverifiedReviewQueue(reader);
        var notes = new PgHumanReviewNoteJournal(db, frozen);
        var notices = new PgReviewerConflictDisclosureJournal(db, notes);
        var packets = new PgUnverifiedConflictPacketJournal(db, notices);
        var center = "test:" + prefix + "-" + Guid.NewGuid().ToString("N");
        var claim = new EvidenceSubmission(Guid.NewGuid(), 0, center, "test:doctor",
            EvidenceSubjectKind.Physician,
            EvidenceKind.PhysicianProfessionalQualificationClaim,
            "test:source-submitter", "test:unverified-proof");
        Assert.Equal(EvidenceIntakeOutcome.ReviewWorkItemCreated,
            (await evidence.SubmitAsync(center, "test:doctor",
                EvidenceSubjectKind.Physician, claim, TestContext.Current.CancellationToken)).Outcome);
        var snapshot = await frozen.CaptureAsync(center, null, TestContext.Current.CancellationToken);
        var review = new HumanReviewNoteCommand(Guid.NewGuid(), 0, center, "test:doctor",
            EvidenceSubjectKind.Physician, 1, snapshot, 0, null,
            "test:claimed-reviewer", "test:unverified-rationale");
        Assert.Equal(HumanReviewNoteOutcome.PendingSourceVerificationRecorded,
            (await notes.RecordAsync(review, TestContext.Current.CancellationToken)).Outcome);
        var notice = new ConflictNoticeCommand(Guid.NewGuid(), 0, center, "test:doctor",
            EvidenceSubjectKind.Physician, 1, review.RequestId,
            "test:conflict-reporter", "test:possible-relationship");
        Assert.Equal(ConflictNoticeOutcome.PotentialRelationshipRecorded,
            (await notices.RecordAsync(notice, TestContext.Current.CancellationToken)));
        var packet = new ConflictPacketCommand(Guid.NewGuid(), center, "test:doctor",
            EvidenceSubjectKind.Physician, 1, review.RequestId, 1, "test:packet-initiator");
        Assert.Equal(ConflictPacketOutcome.UnverifiedPacketPrepared,
            (await packets.PrepareAsync(packet, TestContext.Current.CancellationToken)));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT packet_id FROM internal_test_conflict_packet WHERE request_ref=@request", cn);
        cmd.Parameters.AddWithValue("request", packet.RequestId);
        var packetId = (Guid)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        var receipt = new TechnicalReceiptCommand(Guid.NewGuid(), packetId, center,
            "test:claimed-recipient", "test:simulated-grant-v1", "test:fake-receipt-evidence");
        return (db, reader, notices, packets, notice, receipt);
    }

    private static PgSyntheticPacketReceiptJournal Journal(
        NpgsqlDataSource db, PgUnverifiedConflictPacketJournal packets,
        PgReviewerConflictDisclosureJournal notices, TechnicalReceiptSourceEvidence? source = null) =>
        new(db, packets, notices, source is null ? null : new TestSource(source));

    private static Task<TechnicalReceiptOutcome> Record(
        PgSyntheticPacketReceiptJournal journal, TechnicalReceiptCommand cmd) =>
        journal.RecordAsync(cmd, TestContext.Current.CancellationToken);

    private static async Task<long> Count(NpgsqlDataSource db, string table, Guid packet)
    {
        var sql = table switch {
            "receipt" => "SELECT count(*) FROM internal_test_packet_receipt WHERE packet_id=@id",
            "audit" => "SELECT count(*) FROM internal_test_packet_receipt_audit WHERE packet_id=@id",
            _ => throw new ArgumentException("Unknown synthetic receipt table")
        };
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("id", packet);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task MissingAuthoritySourceFailsBeforeAnyReceiptWrite()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices);
        Assert.Equal(TechnicalReceiptOutcome.AuthoritySourceUnavailable,
            await Record(journal, command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task MissingOrFailingSimulatedSourceNeverClaimsReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var empty = new PgSyntheticPacketReceiptJournal(db, packets, notices, new TestSource(null));
        Assert.Equal(TechnicalReceiptOutcome.AuthoritySourceUnavailable,
            await Record(empty, command));
        var broken = new TestSource(Attestation(command)) { Broken = true };
        Assert.Equal(TechnicalReceiptOutcome.AuthoritySourceUnavailable,
            await Record(new(db, packets, notices, broken), command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task SyntheticAttestationCanWriteOnlyAnUntrustedTechnicalReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        Assert.Equal(TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded,
            await Record(journal, command));
        var saved = await journal.ReadAsync(command.PacketId, TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal(command, saved.Command);
        Assert.Equal(1L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task ExactAndChangedReplayRemainDistinctWithoutDuplicateAudit()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        await Record(journal, command);
        Assert.Equal(TechnicalReceiptOutcome.ExactReplay, await Record(journal, command));
        Assert.Equal(TechnicalReceiptOutcome.ConflictingReplay,
            await Record(journal, command with { ReceiptEvidenceRef = "test:changed-proof" }));
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task DifferentRequestCannotProduceSecondReceiptForSamePacket()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        await Record(journal, command);
        Assert.Equal(TechnicalReceiptOutcome.AlreadyNotedForPacket,
            await Record(journal, command with { RequestId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task CompetingSyntheticReceiptsAreAtomicAndSerialized()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        var result = await Task.WhenAll(Record(journal, command),
            Record(journal, command with { RequestId = Guid.NewGuid() }));
        Assert.Single(result, x => x == TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded);
        Assert.Single(result, x => x == TechnicalReceiptOutcome.AlreadyNotedForPacket);
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task ConcurrentExactRetryCannotCreateSecondReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        var result = await Task.WhenAll(Record(journal, command), Record(journal, command));
        Assert.Single(result, x => x == TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded);
        Assert.Single(result, x => x == TechnicalReceiptOutcome.ExactReplay);
    }

    [Fact]
    public async Task LaterPotentialConflictSupersedesUnacknowledgedPacket()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        await notices.RecordAsync(notice with { RequestId = Guid.NewGuid(),
            ExpectedRevision = 1, RelationshipEvidenceRef = "test:later-relationship" },
            TestContext.Current.CancellationToken);
        var journal = Journal(db, packets, notices, Attestation(command));
        Assert.Equal(TechnicalReceiptOutcome.SupersededSourceRevision,
            await Record(journal, command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task HistoricalExactReplaySurvivesLaterUnverifiedNotices()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        await Record(journal, command);
        await notices.RecordAsync(notice with { RequestId = Guid.NewGuid(),
            ExpectedRevision = 1, RelationshipEvidenceRef = "test:later-relationship" },
            TestContext.Current.CancellationToken);
        Assert.Equal(TechnicalReceiptOutcome.ExactReplay, await Record(journal, command));
        Assert.Equal(1L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task RecipientMayNotBeReviewedPhysicianOrPacketInitiator()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        foreach (var who in new[] { "test:doctor", "test:packet-initiator" })
        {
            var attempt = command with { RecipientClaimRef = who };
            var journal = Journal(db, packets, notices, Attestation(attempt));
            Assert.Equal(TechnicalReceiptOutcome.RecipientIsSubjectOrInitiator,
                await Record(journal, attempt));
        }
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task SimulatedRevocationAndSessionImpersonationAreDenied()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var revoked = Attestation(command) with { SimulatedGrantNotRevoked = false };
        Assert.Equal(TechnicalReceiptOutcome.RecipientUnverified,
            await Record(Journal(db, packets, notices, revoked), command));
        var forged = Attestation(command) with { SessionSubjectRef = "test:other-person" };
        Assert.Equal(TechnicalReceiptOutcome.RecipientUnverified,
            await Record(Journal(db, packets, notices, forged), command));
    }

    [Fact]
    public async Task ScopeMismatchAndStaleGrantVersionDenyReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var other = Attestation(command) with { CenterRef = "test:other-center" };
        Assert.Equal(TechnicalReceiptOutcome.PacketScopeMismatch,
            await Record(Journal(db, packets, notices, other), command));
        var grant = Attestation(command) with { CurrentGrantVersionRef = "test:revoked-v2" };
        Assert.Equal(TechnicalReceiptOutcome.RecipientUnverified,
            await Record(Journal(db, packets, notices, grant), command));
    }

    [Fact]
    public async Task PacketAuditFailureAtomicallyRollsBackSimulatedReceipt()
    {
        var (db, reader, notices, packets, notice, command) =
            await SetupAsync("abort-receipt-audit");
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => Record(journal, command));
        Assert.Equal("TEST_PACKET_RECEIPT_AUDIT_UNAVAILABLE", exception.MessageText);
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(0L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task OrdinarySqlCannotRewriteTechnicalReceiptOrAudit()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        await Record(journal, command);
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach (var sql in new[] {
            "DELETE FROM internal_test_packet_receipt WHERE packet_id=@id",
            "UPDATE internal_test_packet_receipt_audit SET receipt_digest='forged' WHERE packet_id=@id"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("id", command.PacketId);
            var err = await Assert.ThrowsAsync<PostgresException>(async () =>
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("TEST_PACKET_RECEIPT_APPEND_ONLY", err.MessageText);
        }
    }

    [Fact]
    public void ContractHasNoRealHumanAcknowledgementOrReviewerDecision()
    {
        Assert.DoesNotContain(Enum.GetNames<TechnicalReceiptOutcome>(),
            name => name is "Approved" or "Delivered" or "Assigned" or "Acknowledged"
                or "HumanReceiptConfirmed" or "Qualified");
        Assert.DoesNotContain(typeof(SyntheticPacketReceipt).GetProperties(),
            p => p.Name is "ProfessionalStatus" or "MedicalPermission" or "HumanSignature");
    }
}
