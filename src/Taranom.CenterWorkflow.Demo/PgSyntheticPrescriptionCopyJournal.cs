using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// Isolated CI-only PostgreSQL journal for synthetic opaque prescription copies.
internal sealed class PgSyntheticPrescriptionCopyJournal(
    NpgsqlDataSource writer, NpgsqlDataSource reader)
{
    private const string CopyTable = "internal_test_prescription_copy";
    private const string EventTable = "internal_test_prescription_copy_event";
    private const string AuditTable = "internal_test_prescription_copy_audit";
    private const string WriterRole = "taranom_sprint241_rxcopy_writer_test";
    private const string ReaderRole = "taranom_sprint241_rxcopy_reader_test";

    private sealed record EventMetadata(
        Guid RequestId,
        long ExpectedRevision,
        string MemberRef,
        string PrescriptionRef,
        string IssuerRef,
        string SourceRevision,
        DateTimeOffset IssuedAt,
        string DocumentSha256);

    internal static async Task InitializeAsync(NpgsqlDataSource owner)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_prescription_copy (
                prescription_ref text PRIMARY KEY CHECK(prescription_ref LIKE 'test:%'),
                member_ref text NOT NULL CHECK(member_ref LIKE 'test:%'),
                current_revision bigint NOT NULL DEFAULT 0 CHECK(current_revision>=0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_prescription_copy_event (
                prescription_ref text NOT NULL REFERENCES internal_test_prescription_copy(prescription_ref),
                revision bigint NOT NULL CHECK(revision>0),
                request_id uuid NOT NULL,
                command_json text NOT NULL,
                document_bytes bytea NOT NULL CHECK(octet_length(document_bytes)<=4194304),
                event_digest text NOT NULL CHECK(length(event_digest)=64),
                PRIMARY KEY(prescription_ref,revision),
                UNIQUE(prescription_ref,request_id)
            );
            CREATE TABLE IF NOT EXISTS internal_test_prescription_copy_audit (
                prescription_ref text NOT NULL,
                revision bigint NOT NULL,
                request_id uuid NOT NULL,
                event_digest text NOT NULL,
                observed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(prescription_ref,revision),
                FOREIGN KEY(prescription_ref,revision)
                  REFERENCES internal_test_prescription_copy_event(prescription_ref,revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_prescription_copy_append_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              RAISE EXCEPTION 'TEST_PRESCRIPTION_COPY_APPEND_ONLY' USING ERRCODE='P0001';
            END $body$;
            DROP TRIGGER IF EXISTS internal_test_prescription_copy_event_append_guard
              ON internal_test_prescription_copy_event;
            CREATE TRIGGER internal_test_prescription_copy_event_append_guard
            BEFORE UPDATE OR DELETE ON internal_test_prescription_copy_event
            FOR EACH ROW EXECUTE FUNCTION internal_test_prescription_copy_append_guard();
            DROP TRIGGER IF EXISTS internal_test_prescription_copy_audit_append_guard
              ON internal_test_prescription_copy_audit;
            CREATE TRIGGER internal_test_prescription_copy_audit_append_guard
            BEFORE UPDATE OR DELETE ON internal_test_prescription_copy_audit
            FOR EACH ROW EXECUTE FUNCTION internal_test_prescription_copy_append_guard();
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
            throw new InvalidOperationException("PRESCRIPTION_COPY_ROLES_CI_ONLY");

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
            REVOKE ALL ON TABLE {CopyTable},{EventTable},{AuditTable} FROM PUBLIC;
            REVOKE ALL ON TABLE {CopyTable},{EventTable},{AuditTable} FROM {WriterRole},{ReaderRole};
            REVOKE ALL ON SCHEMA public FROM {WriterRole},{ReaderRole};
            GRANT USAGE ON SCHEMA public TO {WriterRole},{ReaderRole};
            GRANT SELECT,INSERT,UPDATE ON {CopyTable} TO {WriterRole};
            GRANT SELECT,INSERT ON {EventTable},{AuditTable} TO {WriterRole};
            GRANT SELECT ON {CopyTable},{EventTable},{AuditTable} TO {ReaderRole};
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
              has_table_privilege(current_user,'internal_test_prescription_copy','SELECT'),
              has_table_privilege(current_user,'internal_test_prescription_copy','INSERT'),
              has_table_privilege(current_user,'internal_test_prescription_copy','UPDATE'),
              has_table_privilege(current_user,'internal_test_prescription_copy','DELETE'),
              has_table_privilege(current_user,'internal_test_prescription_copy_event','SELECT'),
              has_table_privilege(current_user,'internal_test_prescription_copy_event','INSERT'),
              has_table_privilege(current_user,'internal_test_prescription_copy_event','UPDATE'),
              has_table_privilege(current_user,'internal_test_prescription_copy_event','DELETE'),
              has_table_privilege(current_user,'internal_test_prescription_copy_audit','SELECT'),
              has_table_privilege(current_user,'internal_test_prescription_copy_audit','INSERT'),
              has_table_privilege(current_user,'internal_test_prescription_copy_audit','UPDATE'),
              has_table_privilege(current_user,'internal_test_prescription_copy_audit','DELETE')
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
                throw new InvalidOperationException("PRESCRIPTION_COPY_ROLE_PRIVILEGES_UNVERIFIABLE");
        }
        await DeniedAsync(cn, canWrite
            ? "DELETE FROM internal_test_prescription_copy_event WHERE false"
            : "INSERT INTO internal_test_prescription_copy_event(prescription_ref,revision,request_id,command_json,document_bytes,event_digest) VALUES('test:forbidden',1,gen_random_uuid(),'{}',decode('00','hex'),repeat('0',64))");
    }

    private static async Task DeniedAsync(NpgsqlConnection cn, string sql)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(sql, cn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42501") { return; }
        throw new InvalidOperationException("PRESCRIPTION_COPY_FORBIDDEN_SQL_ACCEPTED");
    }

    internal async Task<SyntheticPrescriptionCopyChange> ApplyAsync(
        string prescriptionRef, SyntheticPrescriptionCopyCommand input)
    {
        if (!TestReference(prescriptionRef) || input.PrescriptionRef != prescriptionRef ||
            !TestReference(input.MemberRef))
            return new(SyntheticPrescriptionCopyOutcome.Invalid,
                SyntheticPrescriptionCopySnapshot.Empty);

        await using var cn = await writer.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using (var create = new NpgsqlCommand("""
            INSERT INTO internal_test_prescription_copy(prescription_ref,member_ref)
            VALUES(@ref,@member) ON CONFLICT DO NOTHING
            """, cn, tx))
        {
            create.Parameters.AddWithValue("ref", prescriptionRef);
            create.Parameters.AddWithValue("member", input.MemberRef);
            await create.ExecuteNonQueryAsync();
        }

        var snapshot = await RebuildAsync(cn, tx, prescriptionRef, lockHead: true);
        var change = SyntheticPrescriptionCopyDomain.Apply(snapshot, input);
        if (change.Outcome != SyntheticPrescriptionCopyOutcome.RecordedCopy)
        {
            await tx.RollbackAsync();
            return change;
        }

        var version = change.Snapshot.History[^1];
        var metadata = new EventMetadata(input.RequestId, input.ExpectedRevision,
            input.MemberRef, input.PrescriptionRef, input.IssuerRef,
            input.SourceRevision, input.IssuedAt, input.DocumentSha256);
        var json = JsonSerializer.Serialize(metadata);
        var digest = Digest(prescriptionRef, version.Revision, input.RequestId,
            json, input.DocumentBytes.AsSpan());
        await using (var append = new NpgsqlCommand("""
            INSERT INTO internal_test_prescription_copy_event
              (prescription_ref,revision,request_id,command_json,document_bytes,event_digest)
            VALUES(@ref,@revision,@id,@json,@bytes,@digest)
            """, cn, tx))
        {
            append.Parameters.AddWithValue("ref", prescriptionRef);
            append.Parameters.AddWithValue("revision", version.Revision);
            append.Parameters.AddWithValue("id", input.RequestId);
            append.Parameters.AddWithValue("json", json);
            append.Parameters.AddWithValue("bytes", input.DocumentBytes.ToArray());
            append.Parameters.AddWithValue("digest", digest);
            await append.ExecuteNonQueryAsync();
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_prescription_copy_audit
              (prescription_ref,revision,request_id,event_digest)
            VALUES(@ref,@revision,@id,@digest)
            """, cn, tx))
        {
            audit.Parameters.AddWithValue("ref", prescriptionRef);
            audit.Parameters.AddWithValue("revision", version.Revision);
            audit.Parameters.AddWithValue("id", input.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync();
        }
        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_prescription_copy SET current_revision=@next
            WHERE prescription_ref=@ref AND current_revision=@before
            """, cn, tx))
        {
            update.Parameters.AddWithValue("next", version.Revision);
            update.Parameters.AddWithValue("ref", prescriptionRef);
            update.Parameters.AddWithValue("before", snapshot.Revision);
            if (await update.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("PRESCRIPTION_COPY_REVISION_CAS_FAILED");
        }
        await tx.CommitAsync();
        return change;
    }

    internal async Task<SyntheticPrescriptionCopySnapshot?> ReadAsync(
        string memberRef, string prescriptionRef)
    {
        if (!TestReference(memberRef) || !TestReference(prescriptionRef))
            throw new InvalidOperationException("INVALID_TEST_PRESCRIPTION_SCOPE");
        await using var cn = await reader.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", cn, tx))
            ro.ExecuteNonQuery();

        await using (var scope = new NpgsqlCommand("""
            SELECT member_ref FROM internal_test_prescription_copy
            WHERE prescription_ref=@ref
            """, cn, tx))
        {
            scope.Parameters.AddWithValue("ref", prescriptionRef);
            var storedMember = await scope.ExecuteScalarAsync();
            if (storedMember is null) { await tx.CommitAsync(); return null; }
            if (storedMember is not string member || member != memberRef)
                throw new InvalidOperationException("PRESCRIPTION_COPY_SCOPE_CONFLICT");
        }

        var snapshot = await RebuildAsync(cn, tx, prescriptionRef, lockHead: false);
        await tx.CommitAsync();
        return snapshot;
    }

    private static async Task<SyntheticPrescriptionCopySnapshot> RebuildAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, string prescriptionRef, bool lockHead)
    {
        long head;
        string memberRef;
        var suffix = lockHead ? " FOR UPDATE" : string.Empty;
        await using (var query = new NpgsqlCommand(
            "SELECT member_ref,current_revision FROM internal_test_prescription_copy WHERE prescription_ref=@ref" + suffix,
            cn, tx))
        {
            query.Parameters.AddWithValue("ref", prescriptionRef);
            await using var row = await query.ExecuteReaderAsync();
            if (!await row.ReadAsync())
                throw new InvalidOperationException("PRESCRIPTION_COPY_HEAD_MISSING");
            memberRef = row.GetString(0);
            head = row.GetInt64(1);
        }
        if (!TestReference(memberRef) || head < 0 || head > SyntheticPrescriptionCopyDomain.MaximumHistoryVersions)
            throw new InvalidOperationException("PRESCRIPTION_COPY_HEAD_UNVERIFIABLE");

        var snapshot = SyntheticPrescriptionCopySnapshot.Empty;
        var count = 0L;
        await using (var events = new NpgsqlCommand("""
            SELECT e.revision,e.request_id,e.command_json,e.document_bytes,e.event_digest,
                   a.request_id,a.event_digest
            FROM internal_test_prescription_copy_event e
            LEFT JOIN internal_test_prescription_copy_audit a
              ON a.prescription_ref=e.prescription_ref AND a.revision=e.revision
            WHERE e.prescription_ref=@ref ORDER BY e.revision
            """, cn, tx))
        {
            events.Parameters.AddWithValue("ref", prescriptionRef);
            await using var rows = await events.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                count++;
                var revision = rows.GetInt64(0);
                var id = rows.GetGuid(1);
                var json = rows.GetString(2);
                var bytes = rows.GetFieldValue<byte[]>(3);
                var digest = rows.GetString(4);
                if (rows.IsDBNull(5) || rows.IsDBNull(6) ||
                    revision != count || rows.GetGuid(5) != id ||
                    rows.GetString(6) != digest)
                    throw new InvalidOperationException("PRESCRIPTION_COPY_AUDIT_DIVERGENCE");
                var metadata = JsonSerializer.Deserialize<EventMetadata>(json)
                    ?? throw new InvalidOperationException("PRESCRIPTION_COPY_EVENT_INVALID");
                var command = new SyntheticPrescriptionCopyCommand(metadata.RequestId,
                    metadata.ExpectedRevision, metadata.MemberRef, metadata.PrescriptionRef,
                    metadata.IssuerRef, metadata.SourceRevision, metadata.IssuedAt,
                    ImmutableArray.CreateRange(bytes), metadata.DocumentSha256);
                if (metadata.RequestId != id ||
                    digest != Digest(prescriptionRef, revision, id, json, bytes))
                    throw new InvalidOperationException("PRESCRIPTION_COPY_EVENT_DIGEST_INVALID");
                var change = SyntheticPrescriptionCopyDomain.Apply(snapshot, command);
                if (change.Outcome != SyntheticPrescriptionCopyOutcome.RecordedCopy)
                    throw new InvalidOperationException("PRESCRIPTION_COPY_HISTORY_INVALID");
                snapshot = change.Snapshot;
            }
        }
        if (count != head || snapshot.Revision != head ||
            (head > 0 && snapshot.MemberRef != memberRef))
            throw new InvalidOperationException("PRESCRIPTION_COPY_HEAD_DIVERGENCE");
        return snapshot;
    }

    private static string Digest(
        string prescriptionRef, long revision, Guid requestId, string json, ReadOnlySpan<byte> bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, Encoding.UTF8.GetBytes("TRN-SYNTHETIC-PRESCRIPTION-COPY-v1"));
        Add(hash, Encoding.UTF8.GetBytes(prescriptionRef));
        Add(hash, BitConverter.GetBytes(revision));
        Add(hash, requestId.ToByteArray());
        Add(hash, Encoding.UTF8.GetBytes(json));
        Add(hash, SHA256.HashData(bytes));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Add(IncrementalHash hash, byte[] value)
    {
        hash.AppendData(BitConverter.GetBytes(value.Length));
        hash.AppendData(value);
    }

    private static bool TestReference(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');
}
