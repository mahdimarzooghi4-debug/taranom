using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// A "receipt" is ONLY an internal simulated database observation: no real
// recipient, assignment, notification, consent or legal responsibility.
internal sealed record SyntheticPacketReceipt(
    TechnicalReceiptCommand Command, Guid PacketId,
    string SimulatedIssuerRef, long SimulatedIssuerRevision);

internal sealed class PgSyntheticPacketReceiptJournal(
    NpgsqlDataSource database, PgUnverifiedConflictPacketJournal packets,
    PgReviewerConflictDisclosureJournal disclosures,
    ITechnicalReceiptAuthoritySource? source = null)
{
    internal static async Task InitializeTestSchemaAsync(
        NpgsqlDataSource database, CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_packet_receipt(
                packet_id uuid PRIMARY KEY REFERENCES internal_test_conflict_packet(packet_id),
                request_ref uuid NOT NULL UNIQUE,
                center_ref text NOT NULL,
                recipient_ref text NOT NULL,
                issuer_ref text NOT NULL,
                issuer_revision bigint NOT NULL CHECK (issuer_revision > 0),
                command_json text NOT NULL,
                receipt_digest text NOT NULL,
                recorded_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS internal_test_packet_receipt_audit(
                packet_id uuid PRIMARY KEY REFERENCES internal_test_packet_receipt(packet_id),
                request_ref uuid NOT NULL,
                receipt_digest text NOT NULL,
                recorded_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE OR REPLACE FUNCTION internal_test_packet_receipt_append_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF TG_OP='INSERT' AND TG_TABLE_NAME='internal_test_packet_receipt_audit'
                   AND EXISTS(SELECT 1 FROM internal_test_packet_receipt r
                              WHERE r.packet_id=NEW.packet_id
                                AND r.center_ref LIKE 'test:abort-receipt-audit-%') THEN
                    RAISE EXCEPTION 'TEST_PACKET_RECEIPT_AUDIT_UNAVAILABLE'
                      USING ERRCODE='P0001';
                END IF;
                IF TG_OP='INSERT' THEN RETURN NEW; END IF;
                RAISE EXCEPTION 'TEST_PACKET_RECEIPT_APPEND_ONLY' USING ERRCODE='P0001';
            END; $body$;
            DROP TRIGGER IF EXISTS internal_test_packet_receipt_guard_tr
                ON internal_test_packet_receipt;
            CREATE TRIGGER internal_test_packet_receipt_guard_tr
                BEFORE UPDATE OR DELETE ON internal_test_packet_receipt
                FOR EACH ROW EXECUTE FUNCTION internal_test_packet_receipt_append_guard();
            DROP TRIGGER IF EXISTS internal_test_packet_receipt_audit_guard_tr
                ON internal_test_packet_receipt_audit;
            CREATE TRIGGER internal_test_packet_receipt_audit_guard_tr
                BEFORE INSERT OR UPDATE OR DELETE ON internal_test_packet_receipt_audit
                FOR EACH ROW EXECUTE FUNCTION internal_test_packet_receipt_append_guard();
            """;
        await using var cn = await database.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(ddl, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<TechnicalReceiptOutcome> RecordAsync(
        TechnicalReceiptCommand command, CancellationToken ct = default)
    {
        var initialCheck = await ConflictPacketReceiptGuard.InspectAsync(
            command, source, ct);
        if (initialCheck.Outcome != TechnicalReceiptOutcome.SimulatedPrerequisitesOnly)
            return initialCheck.Outcome;

        UnverifiedConflictPacket packet;
        ConflictNoticeReadModel latest;
        try
        {
            packet = await packets.ReadAsync(command.PacketId, ct);
            if (packet.Command.CenterRef != command.CenterRef)
                return TechnicalReceiptOutcome.PacketScopeMismatch;
            if (command.RecipientClaimRef == packet.Command.SubjectRef ||
                command.RecipientClaimRef == packet.Command.InitiatorClaimRef)
                return TechnicalReceiptOutcome.RecipientIsSubjectOrInitiator;
            latest = await disclosures.ReadAsync(packet.Command.CenterRef,
                packet.Command.SubjectRef, packet.Command.SubjectKind,
                packet.Command.EvidenceRevision, packet.Command.ReviewNoteRequestId, ct);
        }
        catch (InvalidOperationException)
        {
            return TechnicalReceiptOutcome.PacketMissingOrCorrupt;
        }

        await using var cn = await database.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        // Serialize a request globally, including malicious reuse against
        // another packet, without a lock on a nonexistent receipt row.
        await using (var serial = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(hashtextextended(CAST(@request AS text),0))
            """, cn, tx))
        {
            serial.Parameters.AddWithValue("request", command.RequestId.ToString("D"));
            await serial.ExecuteNonQueryAsync(ct);
        }
        await using (var lockPacket = new NpgsqlCommand("""
            SELECT packet_id FROM internal_test_conflict_packet
            WHERE packet_id=@packet FOR UPDATE
            """, cn, tx))
        {
            lockPacket.Parameters.AddWithValue("packet", command.PacketId);
            if (await lockPacket.ExecuteScalarAsync(ct) is null)
                throw new InvalidOperationException("TEST_PACKET_RECEIPT_PACKET_LOST");
        }

        // Replay is a historical fact. Verify paired accepted audit before
        // returning it, including when newer disclosures supersede the packet.
        var replayId = await FindExistingRequestAsync(cn, tx, command.RequestId, ct);
        if (replayId is { } previous)
        {
            var existing = await ReadLockedAsync(cn, tx, previous, ct)
                ?? throw new InvalidOperationException("TEST_PACKET_RECEIPT_CORRUPT");
            // A duplicate request is an immutable historical fact, not proof
            // of a current simulated issuer. Never call a changed source
            // snapshot "ExactReplay"; preserve the original row and audit.
            // This is a single TEST DOUBLE check, not atomic revocation.
            var sameCommand = existing.Command == command;
            var issuerMatchesHistory =
                initialCheck.Evidence is { } reported &&
                existing.SimulatedIssuerRef == reported.SimulatedIssuerRef &&
                existing.SimulatedIssuerRevision == reported.SimulatedIssuerRevision;
            await tx.RollbackAsync(ct);
            return !sameCommand
                ? TechnicalReceiptOutcome.ConflictingReplay
                : !issuerMatchesHistory
                    ? TechnicalReceiptOutcome.HistoricalIssuerVersionDifferentOnReplay
                    : TechnicalReceiptOutcome.ExactReplay;
        }

        if (latest.Revision != packet.Command.ExpectedDisclosureRevision)
        {
            await tx.RollbackAsync(ct);
            return TechnicalReceiptOutcome.SupersededSourceRevision;
        }
        await using (var lockSource = new NpgsqlCommand("""
            SELECT current_revision FROM internal_test_conflict_notice_head
            WHERE center_ref=@center AND subject_ref=@subject AND
                  subject_kind=@kind AND evidence_revision=@evidence
                  AND review_note_request_ref=@note FOR UPDATE
            """, cn, tx))
        {
            lockSource.Parameters.AddWithValue("center", packet.Command.CenterRef);
            lockSource.Parameters.AddWithValue("subject", packet.Command.SubjectRef);
            lockSource.Parameters.AddWithValue("kind", (int)packet.Command.SubjectKind);
            lockSource.Parameters.AddWithValue("evidence", packet.Command.EvidenceRevision);
            lockSource.Parameters.AddWithValue("note", packet.Command.ReviewNoteRequestId);
            var value = await lockSource.ExecuteScalarAsync(ct);
            if (value is not long version ||
                version != latest.Revision ||
                version != packet.Command.ExpectedDisclosureRevision)
            {
                await tx.RollbackAsync(ct);
                return TechnicalReceiptOutcome.SupersededSourceRevision;
            }
        }

        // A test-source grant may change while PostgreSQL source/packet locks
        // are acquired. Revalidate after both locks and before any receipt
        // writes, including the exact request and evidence-reference binding.
        // This is a negative TOCTOU regression guard, NOT a real revocation
        // guarantee: no production trust root or atomic issuer exists.
        var beforeInsert = await ConflictPacketReceiptGuard.InspectAsync(
            command, source, ct);
        if (beforeInsert.Outcome != TechnicalReceiptOutcome.SimulatedPrerequisitesOnly)
        {
            await tx.RollbackAsync(ct);
            return beforeInsert.Outcome;
        }
        // Compare versioned issuer lineage after the original and late checks.
        // A changing synthetic source must not be written as stable; even an
        // unchanged test issuer still grants NO real-world permission.
        if (initialCheck.Evidence is not { } initialIssuer ||
            beforeInsert.Evidence is not { } finalIssuer ||
            initialIssuer.SimulatedIssuerRef != finalIssuer.SimulatedIssuerRef ||
            initialIssuer.SimulatedIssuerRevision != finalIssuer.SimulatedIssuerRevision)
        {
            await tx.RollbackAsync(ct);
            return TechnicalReceiptOutcome.SimulatedIssuerSnapshotChanged;
        }

        // Exactly one accepted technical receipt per immutable packet.
        await using (var existingPacket = new NpgsqlCommand("""
            SELECT packet_id FROM internal_test_packet_receipt WHERE packet_id=@packet
            """, cn, tx))
        {
            existingPacket.Parameters.AddWithValue("packet", command.PacketId);
            if (await existingPacket.ExecuteScalarAsync(ct) is not null)
            {
                await tx.RollbackAsync(ct);
                return TechnicalReceiptOutcome.AlreadyNotedForPacket;
            }
        }

        var json = JsonSerializer.Serialize(command);
        var digest = Digest(command.PacketId, json,
            finalIssuer.SimulatedIssuerRef, finalIssuer.SimulatedIssuerRevision);
        await using (var write = new NpgsqlCommand("""
            INSERT INTO internal_test_packet_receipt
              (packet_id,request_ref,center_ref,recipient_ref,
               issuer_ref,issuer_revision,command_json,receipt_digest)
            VALUES (@packet,@request,@center,@recipient,
                    @issuer,@issuerVersion,@json,@digest)
            """, cn, tx))
        {
            write.Parameters.AddWithValue("packet", command.PacketId);
            write.Parameters.AddWithValue("request", command.RequestId);
            write.Parameters.AddWithValue("center", command.CenterRef);
            write.Parameters.AddWithValue("recipient", command.RecipientClaimRef);
            write.Parameters.AddWithValue("issuer", finalIssuer.SimulatedIssuerRef);
            write.Parameters.AddWithValue("issuerVersion", finalIssuer.SimulatedIssuerRevision);
            write.Parameters.AddWithValue("json", json);
            write.Parameters.AddWithValue("digest", digest);
            await write.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_packet_receipt_audit
              (packet_id,request_ref,receipt_digest)
            VALUES (@packet,@request,@digest)
            """, cn, tx))
        {
            audit.Parameters.AddWithValue("packet", command.PacketId);
            audit.Parameters.AddWithValue("request", command.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return TechnicalReceiptOutcome.SyntheticTechnicalReceiptRecorded;
    }

    internal async Task<SyntheticPacketReceipt?> ReadAsync(
        Guid packetId, CancellationToken ct = default)
    {
        if (packetId == Guid.Empty) throw new ArgumentException("Packet id required.");
        await using var cn = await database.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var receipt = await ReadLockedAsync(cn, tx, packetId, ct);
        await tx.CommitAsync(ct);
        return receipt;
    }

    private static async Task<Guid?> FindExistingRequestAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, Guid requestId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT packet_id FROM internal_test_packet_receipt WHERE request_ref=@request
            """, cn, tx);
        cmd.Parameters.AddWithValue("request", requestId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null ? null : (Guid)result;
    }

    private static async Task<SyntheticPacketReceipt?> ReadLockedAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, Guid packetId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT r.request_ref,r.center_ref,r.recipient_ref,r.command_json,
                   r.receipt_digest,r.issuer_ref,r.issuer_revision,
                   a.request_ref,a.receipt_digest
            FROM internal_test_packet_receipt r
            LEFT JOIN internal_test_packet_receipt_audit a ON a.packet_id=r.packet_id
            WHERE r.packet_id=@packet
            """, cn, tx);
        cmd.Parameters.AddWithValue("packet", packetId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        if (reader.IsDBNull(5) || reader.IsDBNull(6) ||
            reader.IsDBNull(7) || reader.IsDBNull(8))
            throw new InvalidOperationException("TEST_PACKET_RECEIPT_CORRUPT");
        var json = reader.GetString(3);
        TechnicalReceiptCommand command;
        try { command = JsonSerializer.Deserialize<TechnicalReceiptCommand>(json); }
        catch (JsonException)
        { throw new InvalidOperationException("TEST_PACKET_RECEIPT_CORRUPT"); }
        if (!ConflictPacketReceiptGuard.Valid(command) ||
            command.PacketId != packetId ||
            command.RequestId != reader.GetGuid(0) ||
            command.RequestId != reader.GetGuid(7) ||
            command.CenterRef != reader.GetString(1) ||
            command.RecipientClaimRef != reader.GetString(2) ||
            !global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake.IsSyntheticRef(
                reader.GetString(5)) ||
            reader.GetInt64(6) < 1 ||
            reader.GetString(4) != reader.GetString(8) ||
            reader.GetString(4) != Digest(packetId, json,
                reader.GetString(5), reader.GetInt64(6)))
            throw new InvalidOperationException("TEST_PACKET_RECEIPT_CORRUPT");
        return new(command, packetId, reader.GetString(5), reader.GetInt64(6));
    }

    private static string Digest(Guid id, string json,
        string issuer, long issuerVersion) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-technical-receipt.v2", id.ToString("N"), json, issuer,
            issuerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
}
