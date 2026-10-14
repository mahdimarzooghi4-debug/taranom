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
    // Credentials are a TEST-ONLY CI fixture. These do not authorize
    // deployment and must never be connected to real member/support data.
    internal const string RestrictedRuntimeRole = "taranom_sprint101_support_test";
    // TRN-TECH-128: a different database principal for read-only HTTP.
    internal const string RestrictedReaderRole = "taranom_sprint119_support_reader_test";

    internal static async Task<NpgsqlDataSource> CreateRestrictedTestReaderAsync(
        NpgsqlDataSource owner, NpgsqlConnectionStringBuilder admin)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") != "CI_EPHEMERAL_ONLY" ||
            admin.Database != "taranom_sprint64_center" ||
            admin.Host is not ("127.0.0.1" or "localhost") || admin.Port != 5432)
            throw new InvalidOperationException("SUPPORT_READER_TEST_ONLY");

        // Separate ephemeral CI credential: the reader never knows the writer
        // credential, has no membership in the writer role, and cannot SET ROLE.
        var password = Environment.GetEnvironmentVariable("TARANOM_SUPPORT_READER_TEST_PASSWORD");
        if (password is not { Length: >= 40 and <= 96 } ||
            password.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidOperationException("SUPPORT_READER_PASSWORD_UNAVAILABLE");

        const string grant = """
            DO $role$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles
                    WHERE rolname='taranom_sprint119_support_reader_test') THEN
                    CREATE ROLE taranom_sprint119_support_reader_test
                        LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
                END IF;
            END;
            $role$;
            REVOKE ALL ON TABLE internal_test_support_case,
                internal_test_support_event, internal_test_support_audit
                FROM taranom_sprint119_support_reader_test;
            GRANT USAGE ON SCHEMA public TO taranom_sprint119_support_reader_test;
            GRANT SELECT ON TABLE internal_test_support_case,
                internal_test_support_event, internal_test_support_audit
                TO taranom_sprint119_support_reader_test;
            """;
        await using (var conn = await owner.OpenConnectionAsync())
        {
            await using (var cmd = new NpgsqlCommand(grant, conn))
                await cmd.ExecuteNonQueryAsync();
            await using var setPassword = new NpgsqlCommand(
                "ALTER ROLE taranom_sprint119_support_reader_test PASSWORD '" + password + "'", conn);
            await setPassword.ExecuteNonQueryAsync();
        }
        var config = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Username = RestrictedReaderRole, Password = password, Pooling = false
        };
        var source = NpgsqlDataSource.Create(config.ConnectionString);
        try
        {
            await using var conn = await source.OpenConnectionAsync();
            await using var check = new NpgsqlCommand("""
                SELECT current_user, session_user, rolsuper, rolcreatedb, rolcreaterole,
                       rolcanlogin, rolinherit,
                       has_table_privilege(current_user,'internal_test_support_case','SELECT'),
                       has_table_privilege(current_user,'internal_test_support_event','SELECT'),
                       has_table_privilege(current_user,'internal_test_support_audit','SELECT'),
                       has_table_privilege(current_user,'internal_test_support_case','INSERT'),
                       has_table_privilege(current_user,'internal_test_support_case','UPDATE'),
                       has_table_privilege(current_user,'internal_test_support_event','INSERT'),
                       has_table_privilege(current_user,'internal_test_support_audit','INSERT')
                FROM pg_roles WHERE rolname=current_user
                """, conn);
            await using var row = await check.ExecuteReaderAsync();
            if (!await row.ReadAsync() ||
                row.GetString(0) != RestrictedReaderRole ||
                row.GetString(1) != RestrictedReaderRole ||
                row.GetBoolean(2) || row.GetBoolean(3) || row.GetBoolean(4) ||
                !row.GetBoolean(5) || row.GetBoolean(6) ||
                !row.GetBoolean(7) || !row.GetBoolean(8) || !row.GetBoolean(9) ||
                row.GetBoolean(10) || row.GetBoolean(11) ||
                row.GetBoolean(12) || row.GetBoolean(13))
                throw new InvalidOperationException("SUPPORT_READER_PRIVILEGE_MISMATCH");
            return source;
        }
        catch
        {
            await source.DisposeAsync();
            throw;
        }
    }

    internal static async Task<NpgsqlDataSource> CreateRestrictedTestRuntimeAsync(
        NpgsqlDataSource owner, NpgsqlConnectionStringBuilder admin)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") != "CI_EPHEMERAL_ONLY" ||
            admin.Database != "taranom_sprint64_center" ||
            admin.Host is not ("127.0.0.1" or "localhost") || admin.Port != 5432)
            throw new InvalidOperationException("SUPPORT_RESTRICTED_RUNTIME_TEST_ONLY");
        // A fresh, private-to-this-CI-job credential is supplied by the
        // ephemeral workflow. No hardcoded or checked-in role password.
        var password = Environment.GetEnvironmentVariable("TARANOM_SUPPORT_RUNTIME_TEST_PASSWORD");
        if (password is not { Length: >= 40 and <= 96 } ||
            password.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidOperationException("SUPPORT_TEST_PASSWORD_UNAVAILABLE");

        // Initialize DDL above with the isolated schema owner, but never
        // hand that privileged owner datasource to support HTTP handlers.
        // The runtime needs exactly these DML privileges for the existing
        // immutable event+paired-audit journal and version CAS.
        const string grants = """
            DO $role$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='taranom_sprint101_support_test') THEN
                    CREATE ROLE taranom_sprint101_support_test
                        LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
                END IF;
            END;
            $role$;
            REVOKE ALL ON TABLE internal_test_support_case,
                internal_test_support_event, internal_test_support_audit FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO taranom_sprint101_support_test;
            GRANT SELECT, INSERT, UPDATE ON TABLE internal_test_support_case
                TO taranom_sprint101_support_test;
            GRANT SELECT, INSERT ON TABLE internal_test_support_event,
                internal_test_support_audit TO taranom_sprint101_support_test;
            """;
        await using (var conn = await owner.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand(grants, conn))
        {
            await command.ExecuteNonQueryAsync();
            // Token was validated to ASCII alphanumeric, dash and underscore;
            // cannot terminate the quoted SQL literal.
            await using var setPassword = new NpgsqlCommand(
                "ALTER ROLE taranom_sprint101_support_test PASSWORD '" + password + "'", conn);
            await setPassword.ExecuteNonQueryAsync();
        }
        var restricted = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Username = RestrictedRuntimeRole, Password = password,
            Pooling = false
        };
        var runtime = NpgsqlDataSource.Create(restricted.ConnectionString);
        try
        {
            await using var conn = await runtime.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("""
                SELECT current_user, session_user, rolsuper, rolcreatedb, rolcreaterole
                FROM pg_roles WHERE rolname=current_user
                """, conn);
            await using var row = await command.ExecuteReaderAsync();
            if (!await row.ReadAsync() ||
                row.GetString(0) != RestrictedRuntimeRole ||
                row.GetString(1) != RestrictedRuntimeRole ||
                row.GetBoolean(2) || row.GetBoolean(3) || row.GetBoolean(4))
                throw new InvalidOperationException("SUPPORT_RUNTIME_ROLE_NOT_ISOLATED");
            return runtime;
        }
        catch
        {
            await runtime.DisposeAsync();
            throw;
        }
    }

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

    // TRN-TECH-129: opt-in, anonymous and synthetic-only *local test* worklist.
    // This is NOT an operational member directory or real user authorization.
    // Reuse the reader's distinct SELECT-only PostgreSQL principal and verify
    // each listed case using its canonical event + paired audit lineage.
    internal async Task<(IReadOnlyList<(string CaseRef, long Revision, int FollowUps)> Rows,
        bool HasMore)> ReadWorklistAsync(string? afterCaseRef, int limit)
    {
        if (limit is < 1 or > 10 ||
            (afterCaseRef is not null && !IsSynthetic(afterCaseRef)))
            throw new InvalidOperationException("INVALID_TEST_WORKLIST_CURSOR");

        await using var conn = await database.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
            await readOnly.ExecuteNonQueryAsync();

        var refs = new List<string>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT case_ref FROM internal_test_support_case
            WHERE current_revision > 0
              AND (@after = '' OR case_ref COLLATE "C" > @after COLLATE "C")
            ORDER BY case_ref COLLATE "C"
            LIMIT @count
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("after", afterCaseRef ?? string.Empty);
            cmd.Parameters.AddWithValue("count", limit + 1);
            await using var rows = await cmd.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                var reference = rows.GetString(0);
                if (!IsSynthetic(reference))
                    throw new InvalidOperationException("TEST_WORKLIST_UNVERIFIED_REFERENCE");
                refs.Add(reference);
            }
        }

        var verified = new List<(string CaseRef, long Revision, int FollowUps)>();
        // Verify the lookahead row too: corrupt data must fail the *whole*
        // response, rather than skip a damaged item and silently repaginate.
        foreach (var reference in refs)
        {
            var state = await RehydrateAsync(conn, tx, reference, lockCaseRow: false);
            if (state.Revision <= 0 ||
                state.History.Length != state.Revision ||
                state.History.Length - 1 > int.MaxValue)
                throw new InvalidOperationException("TEST_WORKLIST_UNVERIFIABLE");
            verified.Add((reference, state.Revision, state.History.Length - 1));
        }
        await tx.CommitAsync();
        return (verified.Take(limit).ToArray(), verified.Count > limit);
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
