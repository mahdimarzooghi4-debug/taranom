using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using IntakeDomain = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Synthetic-only packet: prepares references for an eventual accountable
// human triage. Not delivered, assigned, cleared, adjudicated or authorized.
internal enum ConflictPacketOutcome
{
    UnverifiedPacketPrepared = 1,
    ExactReplay = 2,
    ConflictingReplay = 3,
    AlreadyPreparedForRevision = 4,
    SourceRevisionChanged = 5,
    NoPotentialDisclosure = 6,
    Invalid = 7
}

internal readonly record struct ConflictPacketCommand(
    Guid RequestId, string CenterRef, string SubjectRef,
    EvidenceSubjectKind SubjectKind, long EvidenceRevision,
    Guid ReviewNoteRequestId, long ExpectedDisclosureRevision,
    string InitiatorClaimRef);

internal sealed record UnverifiedConflictPacket(
    Guid PacketId, ConflictPacketCommand Command,
    IReadOnlyList<ConflictNoticeRecord> CapturedDisclosures);

// Does not call reviewer provenance or choose the reviewer: neither a real
// source of authority nor assignment policy exists. Never wire into live API.
internal sealed class PgUnverifiedConflictPacketJournal(
    NpgsqlDataSource dataSource,
    PgReviewerConflictDisclosureJournal disclosures)
{
    internal static async Task InitializeTestSchemaAsync(
        NpgsqlDataSource db, CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS internal_test_conflict_packet(
                packet_id uuid PRIMARY KEY,
                request_ref uuid NOT NULL UNIQUE,
                center_ref text NOT NULL,
                subject_ref text NOT NULL,
                subject_kind integer NOT NULL,
                evidence_revision bigint NOT NULL,
                review_note_request_ref uuid NOT NULL,
                disclosure_revision bigint NOT NULL CHECK(disclosure_revision > 0),
                initiator_claim_ref text NOT NULL,
                command_json text NOT NULL,
                manifest_json text NOT NULL,
                packet_digest text NOT NULL,
                recorded_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE(center_ref,subject_ref,subject_kind,evidence_revision,
                       review_note_request_ref,disclosure_revision),
                FOREIGN KEY(center_ref,subject_ref,subject_kind,evidence_revision,
                            review_note_request_ref)
                  REFERENCES internal_test_conflict_notice_head
                    (center_ref,subject_ref,subject_kind,evidence_revision,
                     review_note_request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_conflict_packet_audit(
                packet_id uuid PRIMARY KEY REFERENCES internal_test_conflict_packet(packet_id),
                request_ref uuid NOT NULL,
                packet_digest text NOT NULL,
                recorded_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE OR REPLACE FUNCTION internal_test_conflict_packet_guard()
            RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP='INSERT' AND TG_TABLE_NAME='internal_test_conflict_packet_audit'
                   AND EXISTS (
                       SELECT 1 FROM internal_test_conflict_packet p
                       WHERE p.packet_id=NEW.packet_id AND
                             p.center_ref LIKE 'test:abort-packet-audit-%'
                   ) THEN
                    RAISE EXCEPTION 'TEST_CONFLICT_PACKET_AUDIT_UNAVAILABLE'
                      USING ERRCODE='P0001';
                END IF;
                IF TG_OP='INSERT' THEN RETURN NEW; END IF;
                RAISE EXCEPTION 'TEST_CONFLICT_PACKET_APPEND_ONLY' USING ERRCODE='P0001';
            END $fn$;
            DROP TRIGGER IF EXISTS internal_test_conflict_packet_guard_tr
                ON internal_test_conflict_packet;
            CREATE TRIGGER internal_test_conflict_packet_guard_tr
                BEFORE UPDATE OR DELETE ON internal_test_conflict_packet
                FOR EACH ROW EXECUTE FUNCTION internal_test_conflict_packet_guard();
            DROP TRIGGER IF EXISTS internal_test_conflict_packet_audit_guard_tr
                ON internal_test_conflict_packet_audit;
            CREATE TRIGGER internal_test_conflict_packet_audit_guard_tr
                BEFORE INSERT OR UPDATE OR DELETE ON internal_test_conflict_packet_audit
                FOR EACH ROW EXECUTE FUNCTION internal_test_conflict_packet_guard();
            """;
        await using var cn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<ConflictPacketOutcome> PrepareAsync(
        ConflictPacketCommand command, CancellationToken ct = default)
    {
        if (!Valid(command)) return ConflictPacketOutcome.Invalid;

        // Verify the COMPLETE append-only disclosure and audit history first.
        // A reported relationship is an unverified CLAIM, never wrongdoing.
        var current = await disclosures.ReadAsync(command.CenterRef,
            command.SubjectRef, command.SubjectKind,
            command.EvidenceRevision, command.ReviewNoteRequestId, ct);
        if (current.ScreeningStatus !=
            ConflictScreeningStatus.PotentialRelationshipRequiresIndependentReview)
            return ConflictPacketOutcome.NoPotentialDisclosure;

        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        long sourceRevision;
        await using (var locked = new NpgsqlCommand("""
            SELECT current_revision FROM internal_test_conflict_notice_head
            WHERE center_ref=@center AND subject_ref=@subject AND
                  subject_kind=@kind AND evidence_revision=@evidence AND
                  review_note_request_ref=@note FOR UPDATE
            """, cn, tx))
        {
            AddScope(locked, command);
            sourceRevision = (long)(await locked.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("TEST_PACKET_SOURCE_MISSING"));
        }

        // Request ID is globally unique across synthetic centers/subjects:
        // a cross-scope retry must not become a second packet.
        await using (var replay = new NpgsqlCommand("""
            SELECT packet_id,command_json,manifest_json FROM internal_test_conflict_packet
            WHERE request_ref=@request
            """, cn, tx))
        {
            replay.Parameters.AddWithValue("request", command.RequestId);
            await using var reader = await replay.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                // A replay must verify the original accepted packet/audit
                // before returning any claim of a previously accepted write.
                var priorId = reader.GetGuid(0);
                var priorJson = reader.GetString(1);
                ConflictPacketCommand prior;
                try { prior = JsonSerializer.Deserialize<ConflictPacketCommand>(priorJson); }
                catch (JsonException)
                { throw new InvalidOperationException("TEST_CONFLICT_PACKET_CORRUPT"); }
                await reader.CloseAsync();
                await VerifyPacketAsync(cn, tx, priorId, ct);
                await tx.RollbackAsync(ct);
                return prior == command
                    ? ConflictPacketOutcome.ExactReplay
                    : ConflictPacketOutcome.ConflictingReplay;
            }
        }

        if (sourceRevision != current.Revision ||
            sourceRevision != command.ExpectedDisclosureRevision)
        {
            await tx.RollbackAsync(ct);
            return ConflictPacketOutcome.SourceRevisionChanged;
        }

        await using (var existing = new NpgsqlCommand("""
            SELECT packet_id FROM internal_test_conflict_packet
            WHERE center_ref=@center AND subject_ref=@subject AND
                  subject_kind=@kind AND evidence_revision=@evidence AND
                  review_note_request_ref=@note AND disclosure_revision=@revision
            """, cn, tx))
        {
            AddScope(existing, command);
            existing.Parameters.AddWithValue("revision", command.ExpectedDisclosureRevision);
            var existingPacket = await existing.ExecuteScalarAsync(ct);
            if (existingPacket is not null)
            {
                await tx.RollbackAsync(ct);
                return ConflictPacketOutcome.AlreadyPreparedForRevision;
            }
        }

        var packetId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(command);
        var manifest = JsonSerializer.Serialize(current.Disclosures);
        var digest = Digest(packetId, json, manifest);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO internal_test_conflict_packet
              (packet_id,request_ref,center_ref,subject_ref,subject_kind,
               evidence_revision,review_note_request_ref,disclosure_revision,
               initiator_claim_ref,command_json,manifest_json,packet_digest)
            VALUES (@packet,@request,@center,@subject,@kind,@evidence,@note,
                    @revision,@initiator,@json,@manifest,@digest)
            """, cn, tx))
        {
            AddScope(insert, command);
            insert.Parameters.AddWithValue("packet", packetId);
            insert.Parameters.AddWithValue("request", command.RequestId);
            insert.Parameters.AddWithValue("revision", command.ExpectedDisclosureRevision);
            insert.Parameters.AddWithValue("initiator", command.InitiatorClaimRef);
            insert.Parameters.AddWithValue("json", json);
            insert.Parameters.AddWithValue("manifest", manifest);
            insert.Parameters.AddWithValue("digest", digest);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_conflict_packet_audit
                 (packet_id,request_ref,packet_digest)
            VALUES (@packet,@request,@digest)
            """, cn, tx))
        {
            audit.Parameters.AddWithValue("packet", packetId);
            audit.Parameters.AddWithValue("request", command.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return ConflictPacketOutcome.UnverifiedPacketPrepared;
    }

    internal async Task<UnverifiedConflictPacket> ReadAsync(
        Guid packetId, CancellationToken ct = default)
    {
        if (packetId == Guid.Empty)
            throw new ArgumentException("Synthetic packet ID required.");
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var packet = await VerifyPacketAsync(cn, tx, packetId, ct);
        await tx.CommitAsync(ct);
        return packet;
    }

    private static async Task<UnverifiedConflictPacket> VerifyPacketAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, Guid packetId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT p.request_ref,p.center_ref,p.subject_ref,p.subject_kind,
                   p.evidence_revision,p.review_note_request_ref,
                   p.disclosure_revision,p.initiator_claim_ref,p.command_json,
                   p.manifest_json,p.packet_digest,a.request_ref,a.packet_digest
            FROM internal_test_conflict_packet p
            LEFT JOIN internal_test_conflict_packet_audit a ON a.packet_id=p.packet_id
            WHERE p.packet_id=@packet
            """, cn, tx);
        cmd.Parameters.AddWithValue("packet", packetId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("TEST_CONFLICT_PACKET_NOT_FOUND");
        if (reader.IsDBNull(11) || reader.IsDBNull(12))
            throw new InvalidOperationException("TEST_CONFLICT_PACKET_CORRUPT");

        ConflictPacketCommand stored;
        List<ConflictNoticeRecord>? disclosures;
        var json = reader.GetString(8);
        var manifest = reader.GetString(9);
        try
        {
            stored = JsonSerializer.Deserialize<ConflictPacketCommand>(json);
            disclosures = JsonSerializer.Deserialize<List<ConflictNoticeRecord>>(manifest);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("TEST_CONFLICT_PACKET_CORRUPT");
        }

        if (!Valid(stored) || disclosures is not { Count: > 0 } ||
            stored.RequestId != reader.GetGuid(0) ||
            stored.CenterRef != reader.GetString(1) ||
            stored.SubjectRef != reader.GetString(2) ||
            (int)stored.SubjectKind != reader.GetInt32(3) ||
            stored.EvidenceRevision != reader.GetInt64(4) ||
            stored.ReviewNoteRequestId != reader.GetGuid(5) ||
            stored.ExpectedDisclosureRevision != reader.GetInt64(6) ||
            stored.InitiatorClaimRef != reader.GetString(7) ||
            reader.GetGuid(0) != reader.GetGuid(11) ||
            reader.GetString(10) != reader.GetString(12) ||
            reader.GetString(10) != Digest(packetId, json, manifest) ||
            disclosures.Count != stored.ExpectedDisclosureRevision ||
            disclosures.Where((x, i) => x.Revision != i + 1 ||
                x.RequestId == Guid.Empty ||
                !IntakeDomain.IsSyntheticRef(x.ReporterClaimRef) ||
                !IntakeDomain.IsSyntheticRef(x.RelationshipEvidenceRef))
                .Any())
            throw new InvalidOperationException("TEST_CONFLICT_PACKET_CORRUPT");

        return new(packetId, stored, Array.AsReadOnly(disclosures.ToArray()));
    }

    private static bool Valid(ConflictPacketCommand x) =>
        x.RequestId != Guid.Empty &&
        IntakeDomain.IsSyntheticRef(x.CenterRef) &&
        IntakeDomain.IsSyntheticRef(x.SubjectRef) &&
        Enum.IsDefined(x.SubjectKind) && x.EvidenceRevision >= 1 &&
        x.ReviewNoteRequestId != Guid.Empty &&
        x.ExpectedDisclosureRevision >= 1 &&
        IntakeDomain.IsSyntheticRef(x.InitiatorClaimRef);

    private static void AddScope(NpgsqlCommand command, ConflictPacketCommand input)
    {
        command.Parameters.AddWithValue("center", input.CenterRef);
        command.Parameters.AddWithValue("subject", input.SubjectRef);
        command.Parameters.AddWithValue("kind", (int)input.SubjectKind);
        command.Parameters.AddWithValue("evidence", input.EvidenceRevision);
        command.Parameters.AddWithValue("note", input.ReviewNoteRequestId);
    }

    private static string Digest(Guid id, string command, string manifest) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-conflict-packet.v1", id.ToString("N"),
            command, manifest))));
}
