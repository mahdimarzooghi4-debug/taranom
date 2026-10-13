using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-075: ephemeral, reference-only member support journal.
// No PHI, complaints, insurance decisions, actual member identities, or legal evidence.
// All decisions are still delegated to canonical SyntheticSupportIntake.Apply.
internal sealed class PgSyntheticSupportJournal(NpgsqlDataSource database)
{
    internal static async Task InitializeAsync(NpgsqlDataSource data)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS internal_test_support_case (
                case_ref text PRIMARY KEY,
                current_revision bigint NOT NULL DEFAULT 0 CHECK (current_revision >= 0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_support_event (
                case_ref text NOT NULL REFERENCES internal_test_support_case(case_ref),
                revision bigint NOT NULL CHECK (revision > 0),
                request_ref uuid NOT NULL,
                command_json text NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY (case_ref, revision),
                UNIQUE (case_ref, request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_support_audit (
                case_ref text NOT NULL,
                revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY (case_ref, revision),
                FOREIGN KEY (case_ref, revision)
                    REFERENCES internal_test_support_event(case_ref, revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_support_history_guard()
            RETURNS trigger LANGUAGE plpgsql AS $guard$
            BEGIN
                IF TG_OP = 'INSERT' THEN RETURN NEW; END IF;
                RAISE EXCEPTION 'TEST_SUPPORT_APPEND_ONLY' USING ERRCODE='P0001';
            END;
            $guard$;
            DROP TRIGGER IF EXISTS internal_test_support_event_guard ON internal_test_support_event;
            CREATE TRIGGER internal_test_support_event_guard
                BEFORE UPDATE OR DELETE ON internal_test_support_event
                FOR EACH ROW EXECUTE FUNCTION internal_test_support_history_guard();
            DROP TRIGGER IF EXISTS internal_test_support_audit_guard ON internal_test_support_audit;
            CREATE TRIGGER internal_test_support_audit_guard
                BEFORE UPDATE OR DELETE ON internal_test_support_audit
                FOR EACH ROW EXECUTE FUNCTION internal_test_support_history_guard();
            """;
        await using var connection = await data.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<SyntheticSupportSnapshot> ReadAsync(string caseRef)
    {
        EnsureSynthetic(caseRef);
        await using var conn = await database.OpenConnectionAsync();
        // One committed PostgreSQL MVCC snapshot for header, journal and
        // paired audit. Never block behind a writer's case-row FOR UPDATE.
        await using var tx = await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
            await readOnly.ExecuteNonQueryAsync();
        var state = await RehydrateAsync(conn, tx, caseRef, lockCaseRow: false);
        await tx.CommitAsync();
        return state;
    }

    internal async Task<SyntheticSupportChange> ApplyAsync(
        string caseRef, SyntheticSupportCommand command)
    {
        EnsureSynthetic(caseRef);
        await using var conn = await database.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var init = new NpgsqlCommand("""
            INSERT INTO internal_test_support_case(case_ref) VALUES (@case)
            ON CONFLICT DO NOTHING
            """, conn, tx))
        {
            init.Parameters.AddWithValue("case", caseRef);
            await init.ExecuteNonQueryAsync();
        }
        var current = await RehydrateAsync(conn, tx, caseRef);
        var change = SyntheticSupportIntake.Apply(current, command);
        if (change.Outcome != SyntheticSupportOutcome.RecordedUnverified)
        {
            await tx.RollbackAsync();
            return change;
        }

        var next = change.Snapshot.Revision;
        var json = JsonSerializer.Serialize(command);
        var hash = Digest(caseRef, next, json);
        await using (var evt = new NpgsqlCommand("""
            INSERT INTO internal_test_support_event
                (case_ref,revision,request_ref,command_json,event_digest)
            VALUES (@case,@revision,@request,@json,@hash)
            """, conn, tx))
        {
            evt.Parameters.AddWithValue("case", caseRef);
            evt.Parameters.AddWithValue("revision", next);
            evt.Parameters.AddWithValue("request", command.RequestId);
            evt.Parameters.AddWithValue("json", json);
            evt.Parameters.AddWithValue("hash", hash);
            await evt.ExecuteNonQueryAsync();
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_support_audit
                (case_ref,revision,request_ref,event_digest)
            VALUES (@case,@revision,@request,@hash)
            """, conn, tx))
        {
            audit.Parameters.AddWithValue("case", caseRef);
            audit.Parameters.AddWithValue("revision", next);
            audit.Parameters.AddWithValue("request", command.RequestId);
            audit.Parameters.AddWithValue("hash", hash);
            await audit.ExecuteNonQueryAsync();
        }
        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_support_case
            SET current_revision=@next WHERE case_ref=@case AND current_revision=@old
            """, conn, tx))
        {
            update.Parameters.AddWithValue("next", next);
            update.Parameters.AddWithValue("case", caseRef);
            update.Parameters.AddWithValue("old", current.Revision);
            if (await update.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("TEST_SUPPORT_REVISION_CONFLICT");
        }
        await tx.CommitAsync();
        return change;
    }

    private static async Task<SyntheticSupportSnapshot> RehydrateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string caseRef,
        bool lockCaseRow = true)
    {
        long latest;
        // Writers MUST retain case-row locking and CAS. Read-only callers
        // query the same header WITHOUT FOR UPDATE within REPEATABLE READ.
        await using (var header = new NpgsqlCommand(lockCaseRow ? """
            SELECT current_revision FROM internal_test_support_case
            WHERE case_ref=@case FOR UPDATE
            """ : """
            SELECT current_revision FROM internal_test_support_case
            WHERE case_ref=@case
            """, conn, tx))
        {
            header.Parameters.AddWithValue("case", caseRef);
            var raw = await header.ExecuteScalarAsync();
            if (raw is null) return SyntheticSupportSnapshot.Empty;
            latest = (long)raw;
        }
        var state = SyntheticSupportSnapshot.Empty;
        await using (var history = new NpgsqlCommand("""
            SELECT e.revision,e.request_ref,e.command_json,e.event_digest,
                   a.request_ref,a.event_digest
            FROM internal_test_support_event e
            LEFT JOIN internal_test_support_audit a
              ON a.case_ref=e.case_ref AND a.revision=e.revision
            WHERE e.case_ref=@case ORDER BY e.revision
            """, conn, tx))
        {
            history.Parameters.AddWithValue("case", caseRef);
            await using var rows = await history.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                if (rows.IsDBNull(4) || rows.IsDBNull(5))
                    throw new InvalidOperationException("TEST_SUPPORT_JOURNAL_CORRUPT");
                SyntheticSupportCommand cmd;
                try { cmd = JsonSerializer.Deserialize<SyntheticSupportCommand>(rows.GetString(2)); }
                catch (JsonException) { throw new InvalidOperationException("TEST_SUPPORT_JOURNAL_CORRUPT"); }

                if (!string.Equals(cmd.CaseRef, caseRef, StringComparison.Ordinal) ||
                    cmd.RequestId != rows.GetGuid(1) ||
                    cmd.RequestId != rows.GetGuid(4) ||
                    rows.GetInt64(0) != state.Revision + 1 ||
                    rows.GetString(3) != rows.GetString(5) ||
                    rows.GetString(3) != Digest(caseRef, rows.GetInt64(0), rows.GetString(2)))
                    throw new InvalidOperationException("TEST_SUPPORT_JOURNAL_CORRUPT");
                var next = SyntheticSupportIntake.Apply(state, cmd);
                if (next.Outcome != SyntheticSupportOutcome.RecordedUnverified ||
                    next.Snapshot.Revision != rows.GetInt64(0))
                    throw new InvalidOperationException("TEST_SUPPORT_JOURNAL_CORRUPT");
                state = next.Snapshot;
            }
        }
        if (state.Revision != latest)
            throw new InvalidOperationException("TEST_SUPPORT_JOURNAL_CORRUPT");
        return state;
    }

    private static string Digest(string caseRef, long revision, string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            caseRef + "|" + revision.ToString(CultureInfo.InvariantCulture) + "|" + json)));

    private static void EnsureSynthetic(string reference)
    {
        if (!IsSynthetic(reference))
            throw new InvalidOperationException("TEST_SUPPORT_REFERENCE_REQUIRED");
    }

    internal static bool IsSynthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');
}
