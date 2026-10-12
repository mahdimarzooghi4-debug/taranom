using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.ProofingJournal.Postgres;

// A standalone isolated CI-only PostgreSQL journal. It records opaque
// synthetic evidence work items and notes, NEVER real monetary entitlement,
// credentialed reviewers, legal receipts or actual rights to member data.
internal sealed class PgLateEvidenceWorkItemJournal(NpgsqlDataSource source)
{
    internal static async Task InitializeIsolatedTestSchemaAsync(
        NpgsqlDataSource owner, CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_late_evidence_case (
                case_ref text PRIMARY KEY,
                current_revision bigint NOT NULL DEFAULT 0
                  CHECK (current_revision >= 0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_late_evidence_event (
                case_ref text NOT NULL
                  REFERENCES internal_test_late_evidence_case(case_ref),
                revision bigint NOT NULL CHECK(revision > 0),
                request_ref uuid NOT NULL,
                action_code integer NOT NULL CHECK(action_code IN (1,2)),
                command_json text NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY(case_ref,revision),
                UNIQUE(case_ref,request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_late_evidence_audit (
                case_ref text NOT NULL,
                revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                event_digest text NOT NULL,
                observed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(case_ref,revision),
                FOREIGN KEY(case_ref,revision) REFERENCES
                  internal_test_late_evidence_event(case_ref,revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_late_evidence_event_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              RAISE EXCEPTION 'TEST_LATE_EVIDENCE_APPEND_ONLY' USING ERRCODE='P0001';
            END;
            $body$;
            CREATE OR REPLACE FUNCTION internal_test_late_evidence_audit_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              IF TG_OP='INSERT' THEN
                IF NEW.case_ref LIKE 'test:abort-note-audit-%' THEN
                  RAISE EXCEPTION 'TEST_LATE_EVIDENCE_AUDIT_UNAVAILABLE'
                    USING ERRCODE='P0001';
                END IF;
                RETURN NEW;
              END IF;
              RAISE EXCEPTION 'TEST_LATE_EVIDENCE_APPEND_ONLY' USING ERRCODE='P0001';
            END;
            $body$;
            DROP TRIGGER IF EXISTS internal_test_late_evidence_event_guard_trigger
              ON internal_test_late_evidence_event;
            CREATE TRIGGER internal_test_late_evidence_event_guard_trigger
            BEFORE UPDATE OR DELETE ON internal_test_late_evidence_event
            FOR EACH ROW EXECUTE FUNCTION internal_test_late_evidence_event_guard();
            DROP TRIGGER IF EXISTS internal_test_late_evidence_audit_guard_trigger
              ON internal_test_late_evidence_audit;
            CREATE TRIGGER internal_test_late_evidence_audit_guard_trigger
            BEFORE INSERT OR UPDATE OR DELETE ON internal_test_late_evidence_audit
            FOR EACH ROW EXECUTE FUNCTION internal_test_late_evidence_audit_guard();
            """;
        await using var cn = await owner.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(ddl, cn);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task<LateEvidenceChange> ApplyAsync(
        LateEvidenceCommand input, CancellationToken ct = default)
    {
        // Case names and all input strings are separately validated by the
        // reducer. No SQL read or newly persisted case on invalid input.
        var preliminary = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, input);
        if (preliminary.Outcome == LateEvidenceOutcome.Invalid)
            return preliminary;

        await using var cn = await source.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using (var create = new NpgsqlCommand("""
            INSERT INTO internal_test_late_evidence_case (case_ref) VALUES (@case)
            ON CONFLICT DO NOTHING
            """, cn, tx))
        {
            create.Parameters.AddWithValue("case", input.CaseRef);
            await create.ExecuteNonQueryAsync(ct);
        }

        // Head row UPDATE lock serializes competing writers, including
        // newly-created cases on separate PostgreSQL replicas.
        var existing = await ReadLockedAsync(cn, tx, input.CaseRef, ct);
        var next = LateEvidenceWorkItem.Apply(existing, input);
        if (next.Outcome != LateEvidenceOutcome.RecordedUnverified)
        {
            await tx.RollbackAsync(ct);
            return next;
        }
        var revision = next.Snapshot.Revision;
        var json = JsonSerializer.Serialize(input);
        var digest = Digest(input.CaseRef, revision, input.RequestId, input.Action, json);
        await using (var append = new NpgsqlCommand("""
            INSERT INTO internal_test_late_evidence_event
                (case_ref,revision,request_ref,action_code,command_json,event_digest)
            VALUES (@case,@revision,@request,@action,@json,@digest)
            """, cn, tx))
        {
            AddEventParams(append, input, revision, json, digest);
            await append.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_late_evidence_audit
                (case_ref,revision,request_ref,event_digest)
            VALUES (@case,@revision,@request,@digest)
            """, cn, tx))
        {
            audit.Parameters.AddWithValue("case", input.CaseRef);
            audit.Parameters.AddWithValue("revision", revision);
            audit.Parameters.AddWithValue("request", input.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_late_evidence_case
            SET current_revision=@next
            WHERE case_ref=@case AND current_revision=@before
            """, cn, tx))
        {
            update.Parameters.AddWithValue("next", revision);
            update.Parameters.AddWithValue("before", existing.Revision);
            update.Parameters.AddWithValue("case", input.CaseRef);
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("TEST_LATE_EVIDENCE_CAS_FAILED");
        }
        await tx.CommitAsync(ct);
        return next;
    }

    internal async Task<LateEvidenceCaseSnapshot> ReadAsync(
        string caseRef, CancellationToken ct = default)
    {
        if (!Synthetic(caseRef))
            throw new InvalidOperationException("TEST_LATE_EVIDENCE_CASE_REQUIRED");
        await using var cn = await source.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var state = await ReadLockedAsync(cn, tx, caseRef, ct, allowMissing: true);
        await tx.CommitAsync(ct);
        return state;
    }

    private static async Task<LateEvidenceCaseSnapshot> ReadLockedAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, string caseRef,
        CancellationToken ct, bool allowMissing = false)
    {
        long expected;
        await using (var head = new NpgsqlCommand("""
            SELECT current_revision FROM internal_test_late_evidence_case
            WHERE case_ref=@case FOR UPDATE
            """, cn, tx))
        {
            head.Parameters.AddWithValue("case", caseRef);
            var result = await head.ExecuteScalarAsync(ct);
            if (result is null)
            {
                if (allowMissing) return LateEvidenceCaseSnapshot.Empty;
                throw new InvalidOperationException("TEST_LATE_EVIDENCE_CASE_MISSING");
            }
            expected = (long)result;
        }

        var rebuilt = LateEvidenceCaseSnapshot.Empty;
        await using (var query = new NpgsqlCommand("""
            SELECT e.revision,e.request_ref,e.action_code,e.command_json,e.event_digest,
                   a.request_ref,a.event_digest
            FROM internal_test_late_evidence_event e
            LEFT JOIN internal_test_late_evidence_audit a
              ON a.case_ref=e.case_ref AND a.revision=e.revision
            WHERE e.case_ref=@case ORDER BY e.revision
            """, cn, tx))
        {
            query.Parameters.AddWithValue("case", caseRef);
            await using var rows = await query.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct))
            {
                if (rows.IsDBNull(5) || rows.IsDBNull(6))
                    throw new InvalidOperationException("TEST_LATE_EVIDENCE_JOURNAL_CORRUPT");

                var json = rows.GetString(3);
                LateEvidenceCommand input;
                try
                {
                    input = JsonSerializer.Deserialize<LateEvidenceCommand>(json);
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("TEST_LATE_EVIDENCE_JOURNAL_CORRUPT");
                }
                var candidate = LateEvidenceWorkItem.Apply(rebuilt, input);
                var revision = rows.GetInt64(0);
                var requestRef = rows.GetGuid(1);
                var action = (LateEvidenceAction)rows.GetInt32(2);
                var digest = rows.GetString(4);
                if (candidate.Outcome != LateEvidenceOutcome.RecordedUnverified ||
                    candidate.Snapshot.Revision != revision ||
                    !string.Equals(input.CaseRef, caseRef, StringComparison.Ordinal) ||
                    input.RequestId != requestRef ||
                    action != input.Action ||
                    rows.GetGuid(5) != requestRef ||
                    rows.GetString(6) != digest ||
                    digest != Digest(caseRef, revision, requestRef, action, json))
                    throw new InvalidOperationException("TEST_LATE_EVIDENCE_JOURNAL_CORRUPT");
                rebuilt = candidate.Snapshot;
            }
        }
        if (rebuilt.Revision != expected)
            throw new InvalidOperationException("TEST_LATE_EVIDENCE_JOURNAL_CORRUPT");
        return rebuilt;
    }

    private static void AddEventParams(NpgsqlCommand cmd, LateEvidenceCommand input,
        long revision, string json, string digest)
    {
        cmd.Parameters.AddWithValue("case", input.CaseRef);
        cmd.Parameters.AddWithValue("revision", revision);
        cmd.Parameters.AddWithValue("request", input.RequestId);
        cmd.Parameters.AddWithValue("action", (int)input.Action);
        cmd.Parameters.AddWithValue("json", json);
        cmd.Parameters.AddWithValue("digest", digest);
    }

    private static string Digest(string caseRef, long revision, Guid requestId,
        LateEvidenceAction action, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-late-evidence.v1", caseRef,
            revision.ToString(CultureInfo.InvariantCulture),
            requestId.ToString("N"), ((int)action).ToString(CultureInfo.InvariantCulture),
            json))));

    private static bool Synthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
