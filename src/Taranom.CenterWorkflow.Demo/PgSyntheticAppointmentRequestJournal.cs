using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// Isolated CI-only PostgreSQL journal for synthetic appointment request intents.
internal sealed class PgSyntheticAppointmentRequestJournal(
    NpgsqlDataSource writer, NpgsqlDataSource reader)
{
    private const string CaseTable = "internal_test_appointment_request";
    private const string EventTable = "internal_test_appointment_request_event";
    private const string AuditTable = "internal_test_appointment_request_audit";
    private const string WriterRole = "taranom_sprint206_appt_writer_test";
    private const string ReaderRole = "taranom_sprint206_appt_reader_test";

    internal static async Task InitializeAsync(NpgsqlDataSource owner)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_appointment_request (
                request_ref text PRIMARY KEY CHECK(request_ref LIKE 'test:%'),
                current_revision bigint NOT NULL DEFAULT 0 CHECK(current_revision>=0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_appointment_request_event (
                request_ref text NOT NULL REFERENCES internal_test_appointment_request(request_ref),
                revision bigint NOT NULL CHECK(revision>0),
                request_id uuid NOT NULL,
                command_json text NOT NULL,
                event_digest text NOT NULL CHECK(length(event_digest)=64),
                PRIMARY KEY(request_ref,revision),
                UNIQUE(request_ref,request_id)
            );
            CREATE TABLE IF NOT EXISTS internal_test_appointment_request_audit (
                request_ref text NOT NULL,
                revision bigint NOT NULL,
                request_id uuid NOT NULL,
                event_digest text NOT NULL,
                observed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(request_ref,revision),
                FOREIGN KEY(request_ref,revision)
                  REFERENCES internal_test_appointment_request_event(request_ref,revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_appointment_request_append_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              RAISE EXCEPTION 'TEST_APPOINTMENT_REQUEST_APPEND_ONLY' USING ERRCODE='P0001';
            END $body$;
            DROP TRIGGER IF EXISTS internal_test_appointment_request_event_append_guard
              ON internal_test_appointment_request_event;
            CREATE TRIGGER internal_test_appointment_request_event_append_guard
            BEFORE UPDATE OR DELETE ON internal_test_appointment_request_event
            FOR EACH ROW EXECUTE FUNCTION internal_test_appointment_request_append_guard();
            DROP TRIGGER IF EXISTS internal_test_appointment_request_audit_append_guard
              ON internal_test_appointment_request_audit;
            CREATE TRIGGER internal_test_appointment_request_audit_append_guard
            BEFORE UPDATE OR DELETE ON internal_test_appointment_request_audit
            FOR EACH ROW EXECUTE FUNCTION internal_test_appointment_request_append_guard();
            """;
        await using var cn = await owner.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(ddl, cn);
        await cmd.ExecuteNonQueryAsync();
    }

    internal static async Task<(NpgsqlDataSource Writer, NpgsqlDataSource Reader)>
        RestrictedSourcesAsync(NpgsqlDataSource owner, NpgsqlConnectionStringBuilder admin)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") != "CI_EPHEMERAL_ONLY" ||
            admin.Database != "taranom_sprint64_center" ||
            admin.Host is not ("localhost" or "127.0.0.1") || admin.Port != 5432)
            throw new InvalidOperationException("APPOINTMENT_REQUEST_ROLES_CI_ONLY");

        var writerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var readerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var sql = $"""
            DO $roles$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{WriterRole}') THEN
                CREATE ROLE {WriterRole} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
              END IF;
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{ReaderRole}') THEN
                CREATE ROLE {ReaderRole} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
              END IF;
            END;
            $roles$;
            ALTER ROLE {WriterRole} PASSWORD '{writerPassword}';
            ALTER ROLE {ReaderRole} PASSWORD '{readerPassword}';
            REVOKE ALL ON TABLE {CaseTable},{EventTable},{AuditTable} FROM PUBLIC;
            REVOKE ALL ON TABLE {CaseTable},{EventTable},{AuditTable} FROM {WriterRole},{ReaderRole};
            REVOKE ALL ON SCHEMA public FROM {WriterRole},{ReaderRole};
            GRANT USAGE ON SCHEMA public TO {WriterRole},{ReaderRole};
            GRANT SELECT,INSERT,UPDATE ON {CaseTable} TO {WriterRole};
            GRANT SELECT,INSERT ON {EventTable},{AuditTable} TO {WriterRole};
            GRANT SELECT ON {CaseTable},{EventTable},{AuditTable} TO {ReaderRole};
            """;
        await using (var cn = await owner.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand(sql, cn))
            await cmd.ExecuteNonQueryAsync();

        var writer = CreateRestricted(admin, WriterRole, writerPassword);
        var reader = CreateRestricted(admin, ReaderRole, readerPassword);
        await VerifyRoleAsync(writer, WriterRole, canWrite: true);
        await VerifyRoleAsync(reader, ReaderRole, canWrite: false);
        return (writer, reader);
    }

    private static NpgsqlDataSource CreateRestricted(
        NpgsqlConnectionStringBuilder admin, string role, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Username = role, Password = password, Pooling = false
        };
        return NpgsqlDataSource.Create(builder.ConnectionString);
    }

    private static async Task VerifyRoleAsync(
        NpgsqlDataSource source, string role, bool canWrite)
    {
        await using var cn = await source.OpenConnectionAsync();
        const string sql = """
            SELECT current_user,
              has_table_privilege(current_user,'internal_test_appointment_request','SELECT'),
              has_table_privilege(current_user,'internal_test_appointment_request','INSERT'),
              has_table_privilege(current_user,'internal_test_appointment_request','UPDATE'),
              has_table_privilege(current_user,'internal_test_appointment_request','DELETE'),
              has_table_privilege(current_user,'internal_test_appointment_request_event','SELECT'),
              has_table_privilege(current_user,'internal_test_appointment_request_event','INSERT'),
              has_table_privilege(current_user,'internal_test_appointment_request_event','UPDATE'),
              has_table_privilege(current_user,'internal_test_appointment_request_event','DELETE'),
              has_table_privilege(current_user,'internal_test_appointment_request_audit','SELECT'),
              has_table_privilege(current_user,'internal_test_appointment_request_audit','INSERT'),
              has_table_privilege(current_user,'internal_test_appointment_request_audit','UPDATE'),
              has_table_privilege(current_user,'internal_test_appointment_request_audit','DELETE')
            """;
        await using (var cmd = new NpgsqlCommand(sql, cn))
        await using (var row = await cmd.ExecuteReaderAsync())
        {
            if (!await row.ReadAsync() || row.GetString(0) != role ||
                !row.GetBoolean(1) || row.GetBoolean(2) != canWrite ||
                row.GetBoolean(3) != canWrite || row.GetBoolean(4) ||
                !row.GetBoolean(5) || row.GetBoolean(6) != canWrite ||
                row.GetBoolean(7) || row.GetBoolean(8) ||
                !row.GetBoolean(9) || row.GetBoolean(10) != canWrite ||
                row.GetBoolean(11) || row.GetBoolean(12))
                throw new InvalidOperationException("APPOINTMENT_REQUEST_ROLE_PRIVILEGES_UNVERIFIABLE");
        }

        await DeniedAsync(cn, canWrite
            ? "DELETE FROM internal_test_appointment_request_event WHERE false"
            : "INSERT INTO internal_test_appointment_request_event(request_ref,revision,request_id,command_json,event_digest) VALUES('test:forbidden',1,gen_random_uuid(),'{}','x')");
    }

    private static async Task DeniedAsync(NpgsqlConnection cn, string sql)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(sql, cn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42501") { return; }
        throw new InvalidOperationException("APPOINTMENT_REQUEST_FORBIDDEN_SQL_ACCEPTED");
    }

    internal async Task<SyntheticAppointmentRequestChange> ApplyAsync(
        string requestRef, SyntheticAppointmentRequestCommand input)
    {
        await using var cn = await writer.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using (var create = new NpgsqlCommand("""
            INSERT INTO internal_test_appointment_request(request_ref)
            VALUES(@ref) ON CONFLICT DO NOTHING
            """, cn, tx))
        {
            create.Parameters.AddWithValue("ref", requestRef);
            await create.ExecuteNonQueryAsync();
        }

        var snapshot = await RebuildAsync(cn, tx, requestRef, lockHead: true);
        var change = SyntheticAppointmentRequestDomain.Apply(snapshot, input);
        if (change.Outcome != SyntheticAppointmentRequestOutcome.RecordedRequest)
        {
            await tx.RollbackAsync();
            return change;
        }

        var entry = change.Snapshot.History[^1];
        var json = JsonSerializer.Serialize(input);
        var digest = Digest(requestRef, entry.Revision, input.RequestId, json);
        await using (var append = new NpgsqlCommand("""
            INSERT INTO internal_test_appointment_request_event
              (request_ref,revision,request_id,command_json,event_digest)
            VALUES(@ref,@revision,@id,@json,@digest)
            """, cn, tx))
        {
            append.Parameters.AddWithValue("ref", requestRef);
            append.Parameters.AddWithValue("revision", entry.Revision);
            append.Parameters.AddWithValue("id", input.RequestId);
            append.Parameters.AddWithValue("json", json);
            append.Parameters.AddWithValue("digest", digest);
            await append.ExecuteNonQueryAsync();
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_appointment_request_audit
              (request_ref,revision,request_id,event_digest)
            VALUES(@ref,@revision,@id,@digest)
            """, cn, tx))
        {
            audit.Parameters.AddWithValue("ref", requestRef);
            audit.Parameters.AddWithValue("revision", entry.Revision);
            audit.Parameters.AddWithValue("id", input.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync();
        }
        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_appointment_request SET current_revision=@next
            WHERE request_ref=@ref AND current_revision=@before
            """, cn, tx))
        {
            update.Parameters.AddWithValue("next", entry.Revision);
            update.Parameters.AddWithValue("ref", requestRef);
            update.Parameters.AddWithValue("before", snapshot.Revision);
            if (await update.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("APPOINTMENT_REQUEST_REVISION_CAS_FAILED");
        }
        await tx.CommitAsync();
        return change;
    }

    // Read-only, bounded center queue. Every candidate is rebuilt from its
    // event and audit rows before inclusion; no member identity is projected.
    internal async Task<(IReadOnlyList<SyntheticAppointmentQueueRow> Requests,
        bool HasMore, string? NextAfterRequestRef)> ReadForCenterAsync(
        string centerRef, string? departmentRef, string? requestedDoctorRef,
        string? afterRequestRef, int limit)
    {
        if (!TestReference(centerRef) ||
            (departmentRef is not null && !TestReference(departmentRef)) ||
            (requestedDoctorRef is not null && !TestReference(requestedDoctorRef)) ||
            (afterRequestRef is not null && !TestReference(afterRequestRef)) ||
            limit is < 1 or > 10)
            throw new InvalidOperationException("INVALID_APPOINTMENT_QUEUE_CURSOR");

        const int scanLimit = 513;
        await using var cn = await reader.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", cn, tx))
            await ro.ExecuteNonQueryAsync();

        var refs = new List<string>();
        await using (var query = new NpgsqlCommand("""
            SELECT request_ref FROM internal_test_appointment_request
            WHERE current_revision>0
              AND (@after='' OR request_ref COLLATE "C" > @after COLLATE "C")
            ORDER BY request_ref COLLATE "C"
            LIMIT @max
            """, cn, tx))
        {
            query.Parameters.AddWithValue("after", afterRequestRef ?? string.Empty);
            query.Parameters.AddWithValue("max", scanLimit);
            await using var rows = await query.ExecuteReaderAsync();
            while (await rows.ReadAsync()) refs.Add(rows.GetString(0));
        }

        var matching = new List<SyntheticAppointmentQueueRow>();
        foreach (var requestRef in refs)
        {
            if (!TestReference(requestRef))
                throw new InvalidOperationException("APPOINTMENT_QUEUE_REFERENCE_UNVERIFIABLE");
            var snapshot = await RebuildAsync(cn, tx, requestRef, lockHead: false);
            if (snapshot.Revision < 1 || snapshot.History.Length != snapshot.Revision)
                throw new InvalidOperationException("APPOINTMENT_QUEUE_HISTORY_UNVERIFIABLE");
            var latest = snapshot.History[^1].Command;
            if (!TestReference(latest.CenterRef) ||
                !TestReference(latest.DepartmentRef) ||
                !TestReference(latest.RequestedDoctorRef) ||
                !TestReference(latest.SlotRef))
                throw new InvalidOperationException("APPOINTMENT_QUEUE_PAYLOAD_UNVERIFIABLE");
            if (latest.CenterRef != centerRef ||
                (departmentRef is not null && latest.DepartmentRef != departmentRef) ||
                (requestedDoctorRef is not null && latest.RequestedDoctorRef != requestedDoctorRef))
                continue;
            matching.Add(new SyntheticAppointmentQueueRow(requestRef,
                latest.CenterRef, snapshot.Revision, latest.DepartmentRef,
                latest.RequestedDoctorRef, latest.SlotRef));
            if (matching.Count > limit) break;
        }

        var hasMore = matching.Count > limit;
        if (!hasMore && refs.Count == scanLimit)
            throw new InvalidOperationException("APPOINTMENT_QUEUE_SCAN_INCOMPLETE");
        var page = matching.Take(limit).ToArray();
        await tx.CommitAsync();
        return (page, hasMore, hasMore ? page[^1].RequestRef : null);
    }

    private static bool TestReference(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');

    // Read-only, bounded member history. The supplied reference is synthetic
    // and does not authenticate or authorize a real member.
    internal async Task<(IReadOnlyList<SyntheticAppointmentQueueRow> Requests,
        bool HasMore, string? NextAfterRequestRef)> ReadForMemberAsync(
        string memberRef, string? afterRequestRef, int limit)
    {
        if (!TestReference(memberRef) ||
            (afterRequestRef is not null && !TestReference(afterRequestRef)) ||
            limit is < 1 or > 10)
            throw new InvalidOperationException("INVALID_APPOINTMENT_HISTORY_CURSOR");

        const int scanLimit = 513;
        await using var cn = await reader.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", cn, tx))
            await ro.ExecuteNonQueryAsync();

        var refs = new List<string>();
        await using (var query = new NpgsqlCommand("""
            SELECT request_ref FROM internal_test_appointment_request
            WHERE current_revision>0
              AND (@after='' OR request_ref COLLATE "C" > @after COLLATE "C")
            ORDER BY request_ref COLLATE "C"
            LIMIT @max
            """, cn, tx))
        {
            query.Parameters.AddWithValue("after", afterRequestRef ?? string.Empty);
            query.Parameters.AddWithValue("max", scanLimit);
            await using var rows = await query.ExecuteReaderAsync();
            while (await rows.ReadAsync()) refs.Add(rows.GetString(0));
        }

        var matching = new List<SyntheticAppointmentQueueRow>();
        foreach (var requestRef in refs)
        {
            if (!TestReference(requestRef))
                throw new InvalidOperationException("APPOINTMENT_HISTORY_REFERENCE_UNVERIFIABLE");
            var snapshot = await RebuildAsync(cn, tx, requestRef, lockHead: false);
            if (snapshot.Revision < 1 || snapshot.History.Length != snapshot.Revision)
                throw new InvalidOperationException("APPOINTMENT_HISTORY_LINEAGE_UNVERIFIABLE");
            var latest = snapshot.History[^1].Command;
            if (!TestReference(latest.MemberRef) || !TestReference(latest.CenterRef) ||
                !TestReference(latest.DepartmentRef) || !TestReference(latest.RequestedDoctorRef) ||
                !TestReference(latest.SlotRef))
                throw new InvalidOperationException("APPOINTMENT_HISTORY_PAYLOAD_UNVERIFIABLE");
            if (latest.MemberRef != memberRef) continue;
            matching.Add(new SyntheticAppointmentQueueRow(requestRef,
                latest.CenterRef, snapshot.Revision, latest.DepartmentRef,
                latest.RequestedDoctorRef, latest.SlotRef));
            if (matching.Count > limit) break;
        }

        var hasMore = matching.Count > limit;
        if (!hasMore && refs.Count == scanLimit)
            throw new InvalidOperationException("APPOINTMENT_HISTORY_SCAN_INCOMPLETE");
        var page = matching.Take(limit).ToArray();
        await tx.CommitAsync();
        return (page, hasMore, hasMore ? page[^1].RequestRef : null);
    }

    internal async Task<SyntheticAppointmentRequestSnapshot> ReadAsync(string requestRef)
    {
        await using var cn = await reader.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", cn, tx))
            await ro.ExecuteNonQueryAsync();
        var snapshot = await RebuildAsync(cn, tx, requestRef, lockHead: false);
        await tx.CommitAsync();
        return snapshot;
    }

    private static async Task<SyntheticAppointmentRequestSnapshot> RebuildAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, string requestRef, bool lockHead)
    {
        long expected;
        await using (var head = new NpgsqlCommand(lockHead ? """
            SELECT current_revision FROM internal_test_appointment_request
            WHERE request_ref=@ref FOR UPDATE
            """ : """
            SELECT current_revision FROM internal_test_appointment_request
            WHERE request_ref=@ref
            """, cn, tx))
        {
            head.Parameters.AddWithValue("ref", requestRef);
            var value = await head.ExecuteScalarAsync();
            if (value is null) return SyntheticAppointmentRequestSnapshot.Empty;
            expected = (long)value;
        }

        var snapshot = SyntheticAppointmentRequestSnapshot.Empty;
        await using (var query = new NpgsqlCommand("""
            SELECT e.revision,e.request_id,e.command_json,e.event_digest,
                   a.request_id,a.event_digest
            FROM internal_test_appointment_request_event e
            LEFT JOIN internal_test_appointment_request_audit a
              ON a.request_ref=e.request_ref AND a.revision=e.revision
            WHERE e.request_ref=@ref ORDER BY e.revision
            """, cn, tx))
        {
            query.Parameters.AddWithValue("ref", requestRef);
            await using var rows = await query.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                if (rows.IsDBNull(4) || rows.IsDBNull(5))
                    throw new InvalidOperationException("APPOINTMENT_REQUEST_AUDIT_MISSING");
                SyntheticAppointmentRequestCommand input;
                try { input = JsonSerializer.Deserialize<SyntheticAppointmentRequestCommand>(rows.GetString(2)); }
                catch (JsonException) { throw new InvalidOperationException("APPOINTMENT_REQUEST_CORRUPT"); }
                var revision = rows.GetInt64(0);
                var id = rows.GetGuid(1);
                var digest = rows.GetString(3);
                if (rows.GetGuid(4) != id || rows.GetString(5) != digest ||
                    digest != Digest(requestRef, revision, id, rows.GetString(2)) ||
                    input.RequestId != id || revision != snapshot.Revision + 1)
                    throw new InvalidOperationException("APPOINTMENT_REQUEST_LINEAGE_UNVERIFIABLE");
                var next = SyntheticAppointmentRequestDomain.Apply(snapshot, input);
                if (next.Outcome != SyntheticAppointmentRequestOutcome.RecordedRequest ||
                    next.Snapshot.Revision != revision)
                    throw new InvalidOperationException("APPOINTMENT_REQUEST_REPLAY_UNVERIFIABLE");
                snapshot = next.Snapshot;
            }
        }
        if (snapshot.Revision != expected)
            throw new InvalidOperationException("APPOINTMENT_REQUEST_HEAD_UNVERIFIABLE");
        return snapshot;
    }

    private static string Digest(string requestRef, long revision, Guid id, string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            requestRef + "\n" + revision + "\n" + id.ToString("D") + "\n" + json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}


// Queue row deliberately omits the member reference. The retained fields are
// synthetic routing intent only and convey no provider/availability authority.
internal readonly record struct SyntheticAppointmentQueueRow(
    string RequestRef,
    string CenterRef,
    long Revision,
    string DepartmentRef,
    string RequestedDoctorRef,
    string SlotRef);
