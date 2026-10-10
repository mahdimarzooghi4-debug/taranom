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
        public Task<TechnicalReceiptSourceEvidence?> ResolveAsync(Guid packet, Guid request, CancellationToken ct)
        {
            if (Broken) throw new InvalidOperationException("simulated source outage");
            return Task.FromResult(item);
        }
    }

    private static TechnicalReceiptSourceEvidence Attestation(
        TechnicalReceiptCommand command) => new(command.PacketId, command.RequestId, command.CenterRef,
            command.RecipientClaimRef, command.RecipientClaimRef,
            command.GrantVersionRef, command.ReceiptEvidenceRef,
            "test:simulated-issuer", 1,
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
        var changed = command with { ReceiptEvidenceRef = "test:changed-proof" };
        Assert.Equal(TechnicalReceiptOutcome.ConflictingReplay,
            await Record(Journal(db, packets, notices, Attestation(changed)), changed));
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task DifferentRequestCannotProduceSecondReceiptForSamePacket()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        await Record(journal, command);
        var second = command with { RequestId = Guid.NewGuid() };
        Assert.Equal(TechnicalReceiptOutcome.AlreadyNotedForPacket,
            await Record(Journal(db, packets, notices, Attestation(second)), second));
    }

    [Fact]
    public async Task CompetingSyntheticReceiptsAreAtomicAndSerialized()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var journal = Journal(db, packets, notices, Attestation(command));
        var competing = command with { RequestId = Guid.NewGuid() };
        var secondJournal = Journal(db, packets, notices, Attestation(competing));
        var result = await Task.WhenAll(Record(journal, command),
            Record(secondJournal, competing));
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


    private sealed class TwoStageSource(
        TechnicalReceiptSourceEvidence initial,
        TechnicalReceiptSourceEvidence? after,
        bool failAfter = false) : ITechnicalReceiptAuthoritySource
    {
        internal int Calls { get; private set; }
        public Task<TechnicalReceiptSourceEvidence?> ResolveAsync(Guid packetId, Guid requestId, CancellationToken ct)
        {
            Calls++;
            if (Calls > 1 && failAfter)
                throw new InvalidOperationException("simulated revocation-source outage");
            return Task.FromResult(Calls == 1
                ? (TechnicalReceiptSourceEvidence?)initial : after);
        }
    }

    [Fact]
    public async Task SourceAnswerIsCheckedAgainBeforeAcceptingNewReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var correct = Attestation(command);
        var changing = new TwoStageSource(correct, correct with {
            SimulatedGrantNotRevoked = false
        });
        var journal = new PgSyntheticPacketReceiptJournal(db, packets, notices, changing);
        Assert.Equal(TechnicalReceiptOutcome.RecipientUnverified,
            await Record(journal, command));
        Assert.Equal(2, changing.Calls);
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(0L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task SecondSourceOutageBlocksWriteAfterInitiallyPositiveResult()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var changing = new TwoStageSource(Attestation(command), null, true);
        Assert.Equal(TechnicalReceiptOutcome.AuthoritySourceUnavailable,
            await Record(new(db, packets, notices, changing), command));
        Assert.Equal(2, changing.Calls);
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task SecondSourceMissingAnswerBlocksWrite()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var changing = new TwoStageSource(Attestation(command), null);
        Assert.Equal(TechnicalReceiptOutcome.AuthoritySourceUnavailable,
            await Record(new(db, packets, notices, changing), command));
        Assert.Equal(2, changing.Calls);
        Assert.Equal(0L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task SecondSourceChangedDelegationVersionBlocksWrite()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var valid = Attestation(command);
        var changing = new TwoStageSource(valid, valid with {
            CurrentGrantVersionRef = "test:grant-replaced-v2"
        });
        Assert.Equal(TechnicalReceiptOutcome.RecipientUnverified,
            await Record(new(db, packets, notices, changing), command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task SecondSourceChangedRecipientBlocksWrite()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var valid = Attestation(command);
        var changing = new TwoStageSource(valid, valid with {
            ResolvedRecipientRef = "test:someone-else"
        });
        Assert.Equal(TechnicalReceiptOutcome.RecipientUnverified,
            await Record(new(db, packets, notices, changing), command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task IssuerMustBindRequestAndExactReceiptEvidence(
        bool changeRequest, bool changeEvidence)
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var invalid = Attestation(command) with {
            RequestId = changeRequest ? Guid.NewGuid() : command.RequestId,
            ReceiptEvidenceRef = changeEvidence ? "test:other-receipt" : command.ReceiptEvidenceRef
        };
        Assert.Equal(changeRequest
                ? TechnicalReceiptOutcome.PacketScopeMismatch
                : TechnicalReceiptOutcome.RecipientUnverified,
            await Record(Journal(db, packets, notices, invalid), command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task SecondIssuerRebindToDifferentRequestIsDenied()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var valid = Attestation(command);
        var changing = new TwoStageSource(valid, valid with {
            RequestId = Guid.NewGuid()
        });
        Assert.Equal(TechnicalReceiptOutcome.PacketScopeMismatch,
            await Record(new(db, packets, notices, changing), command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task StableSourceMustPassBothChecksAndRetainOneReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var valid = Attestation(command);
        var stable = new TwoStageSource(valid, valid);
        Assert.Equal(TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded,
            await Record(new(db, packets, notices, stable), command));
        Assert.Equal(2, stable.Calls);
        Assert.Equal(1L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
    }

    [Theory]
    [InlineData("real:issuer", 1)]
    [InlineData("test:", 1)]
    [InlineData("test:valid-issuer", 0)]
    [InlineData("test:valid-issuer", -1)]
    public async Task MissingOrMalformedSimulatedIssuerVersionIsRejected(
        string issuer, long version)
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var bad = Attestation(command) with {
            SimulatedIssuerRef = issuer, SimulatedIssuerRevision = version
        };
        Assert.Equal(TechnicalReceiptOutcome.RecipientUnverified,
            await Record(Journal(db, packets, notices, bad), command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
    }

    [Fact]
    public async Task ChangedIssuerIdentityUnderSourceLocksRejectsNewReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var a = Attestation(command);
        var source = new TwoStageSource(a, a with {
            SimulatedIssuerRef = "test:different-issuer"
        });
        Assert.Equal(TechnicalReceiptOutcome.SimulatedIssuerSnapshotChanged,
            await Record(new(db, packets, notices, source), command));
        Assert.Equal(2, source.Calls);
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(0L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task ChangedIssuerRevisionUnderSourceLocksRejectsNewReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var a = Attestation(command);
        var source = new TwoStageSource(a, a with {
            SimulatedIssuerRevision = a.SimulatedIssuerRevision + 1
        });
        Assert.Equal(TechnicalReceiptOutcome.SimulatedIssuerSnapshotChanged,
            await Record(new(db, packets, notices, source), command));
        Assert.Equal(0L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(0L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task StableIssuerRevisionIsDurablyStoredAndCheckedWithAudit()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var a = Attestation(command) with { SimulatedIssuerRevision = 35 };
        var source = new TwoStageSource(a, a);
        var store = new PgSyntheticPacketReceiptJournal(db, packets, notices, source);
        Assert.Equal(TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded,
            await Record(store, command));
        var accepted = await store.ReadAsync(command.PacketId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(accepted);
        Assert.Equal(a.SimulatedIssuerRef, accepted.SimulatedIssuerRef);
        Assert.Equal(35L, accepted.SimulatedIssuerRevision);
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task HistoricalInspectorDeniesWithoutIssuerBeforeReadingReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var store = Journal(db, packets, notices, Attestation(command));
        await Record(store, command);
        var inspector = new PgSyntheticReceiptHistoryInspector(store);
        Assert.Equal(SyntheticReceiptHistoryStatus.SourceUnavailable,
            await inspector.InspectAsync(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HistoricalInspectorReportsMatchingSyntheticVersionNotAuthority()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var attested = Attestation(command);
        var store = Journal(db, packets, notices, attested);
        await Record(store, command);
        var inspector = new PgSyntheticReceiptHistoryInspector(
            store, new TestSource(attested));
        Assert.Equal(SyntheticReceiptHistoryStatus.SimulatedVersionMatchesHistoryOnly,
            await inspector.InspectAsync(command, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Enum.GetNames<SyntheticReceiptHistoryStatus>(),
            x => x is "Authorized" or "Approved" or "Cleared" or "ReceiptAcceptedByHuman");
    }

    [Fact]
    public async Task HistoricalInspectorRejectsIssuerVersionChangeWithoutRewritingHistory()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var a = Attestation(command);
        var store = Journal(db, packets, notices, a);
        await Record(store, command);
        var changed = a with { SimulatedIssuerRevision = 2 };
        var inspector = new PgSyntheticReceiptHistoryInspector(
            store, new TestSource(changed));
        Assert.Equal(SyntheticReceiptHistoryStatus.HistoricalIssuerVersionDifferent,
            await inspector.InspectAsync(command, TestContext.Current.CancellationToken));
        var historic = await store.ReadAsync(command.PacketId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(historic);
        Assert.Equal(1L, historic.SimulatedIssuerRevision);
    }

    [Fact]
    public async Task HistoricalInspectorRejectsIssuerChangeEvenIfRevisionReused()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var a = Attestation(command);
        var store = Journal(db, packets, notices, a);
        await Record(store, command);
        var changed = a with { SimulatedIssuerRef = "test:other-issuer" };
        Assert.Equal(SyntheticReceiptHistoryStatus.HistoricalIssuerVersionDifferent,
            await new PgSyntheticReceiptHistoryInspector(
                store, new TestSource(changed)).InspectAsync(
                    command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HistoricalInspectorRejectsRevokedSourceWithoutReadingAsCurrent()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var a = Attestation(command);
        var store = Journal(db, packets, notices, a);
        await Record(store, command);
        var revoked = a with { SimulatedGrantNotRevoked = false };
        Assert.Equal(SyntheticReceiptHistoryStatus.RecipientOrGrantUnverified,
            await new PgSyntheticReceiptHistoryInspector(
                store, new TestSource(revoked)).InspectAsync(
                    command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HistoricalInspectorRejectsForgedRequestAndReceiptEvidence()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var a = Attestation(command);
        var store = Journal(db, packets, notices, a);
        await Record(store, command);
        var changed = command with { ReceiptEvidenceRef = "test:forged-evidence" };
        Assert.Equal(SyntheticReceiptHistoryStatus.RecordedCommandMismatch,
            await new PgSyntheticReceiptHistoryInspector(
                store, new TestSource(Attestation(changed))).InspectAsync(
                    changed, TestContext.Current.CancellationToken));
        var otherRequest = command with { RequestId = Guid.NewGuid() };
        Assert.Equal(SyntheticReceiptHistoryStatus.RecordedCommandMismatch,
            await new PgSyntheticReceiptHistoryInspector(
                store, new TestSource(Attestation(otherRequest))).InspectAsync(
                    otherRequest, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HistoricalInspectorDoesNotInventMissingReceipt()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var store = Journal(db, packets, notices, Attestation(command));
        Assert.Equal(SyntheticReceiptHistoryStatus.NoHistoricalReceipt,
            await new PgSyntheticReceiptHistoryInspector(
                store, new TestSource(Attestation(command))).InspectAsync(
                    command, TestContext.Current.CancellationToken));
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayDetectsChangedHistoricalIssuerWithoutRewritingOriginal(
        bool changeIssuerIdentity)
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var original = Attestation(command) with { SimulatedIssuerRevision = 7 };
        var stored = Journal(db, packets, notices, original);
        Assert.Equal(TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded,
            await Record(stored, command));
        var changed = changeIssuerIdentity
            ? original with { SimulatedIssuerRef = "test:replacement-issuer" }
            : original with { SimulatedIssuerRevision = 8 };
        Assert.Equal(TechnicalReceiptOutcome.HistoricalIssuerVersionDifferentOnReplay,
            await Record(Journal(db, packets, notices, changed), command));
        var read = await stored.ReadAsync(command.PacketId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(read);
        Assert.Equal(original.SimulatedIssuerRef, read.SimulatedIssuerRef);
        Assert.Equal(original.SimulatedIssuerRevision, read.SimulatedIssuerRevision);
        Assert.Equal(1L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
        Assert.Equal(SyntheticReceiptHistoryStatus.HistoricalIssuerVersionDifferent,
            await new PgSyntheticReceiptHistoryInspector(
                stored, new TestSource(changed)).InspectAsync(
                    command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplayChangedCommandTakesPrecedenceOverIssuerMismatch()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        Assert.Equal(TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded,
            await Record(Journal(db, packets, notices, Attestation(command)), command));
        var competing = command with { ReceiptEvidenceRef = "test:different-evidence" };
        var newIssuer = Attestation(competing) with {
            SimulatedIssuerRef = "test:new-issuer", SimulatedIssuerRevision = 100
        };
        Assert.Equal(TechnicalReceiptOutcome.ConflictingReplay,
            await Record(Journal(db, packets, notices, newIssuer), competing));
        Assert.Equal(1L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task ReplayWithRevokedSyntheticClaimFailsClosedWithoutChangingHistory()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        Assert.Equal(TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded,
            await Record(Journal(db, packets, notices, Attestation(command)), command));
        var invalid = Attestation(command) with { SimulatedGrantNotRevoked = false };
        Assert.Equal(TechnicalReceiptOutcome.RecipientUnverified,
            await Record(Journal(db, packets, notices, invalid), command));
        Assert.Equal(TechnicalReceiptOutcome.AuthoritySourceUnavailable,
            await Record(Journal(db, packets, notices), command));
        Assert.Equal(1L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
    }

    [Fact]
    public async Task ConcurrentChangedIssuerReplaysRemainReadOnlyAndNonAuthorizing()
    {
        var (db, reader, notices, packets, notice, command) = await SetupAsync();
        await using var _ = db; await using var __ = reader;
        var original = Attestation(command);
        Assert.Equal(TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded,
            await Record(Journal(db, packets, notices, original), command));
        var stale = Journal(db, packets, notices, original with {
            SimulatedIssuerRevision = 2
        });
        var results = await Task.WhenAll(Record(stale, command), Record(stale, command));
        Assert.All(results, result =>
            Assert.Equal(TechnicalReceiptOutcome.HistoricalIssuerVersionDifferentOnReplay, result));
        Assert.Equal(1L, await Count(db, "receipt", command.PacketId));
        Assert.Equal(1L, await Count(db, "audit", command.PacketId));
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
