using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.ProofingJournal.Postgres;

// Strictly isolated synthetic PostgreSQL exercise; NOT a deployable identity,
// membership or KYC adapter. No real applicant identifier is permitted.
// The review commands contain only enums, revisions, booleans and a random UUID.
// Table names, roles and schema are isolated to the ephemeral CI database.
internal sealed class PgProofingCaseJournal(NpgsqlDataSource writer,NpgsqlDataSource reader)
{
    // TRN-TECH-173: proofing writer and reader have disjoint least-privilege
    // CI-only PostgreSQL connections, never the schema-owner connection.
    private const string WriterRole="taranom_sprint164_proof_writer_test";
    private const string ReaderRole="taranom_sprint164_proof_reader_test";

    internal static async Task<(NpgsqlDataSource Writer,NpgsqlDataSource Reader)>
        RestrictedTestSourcesAsync(
            NpgsqlDataSource owner,NpgsqlConnectionStringBuilder admin)
    {
        if(Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY")!="CI_EPHEMERAL_ONLY"||
            admin.Database!="taranom_sprint64_center"||
            admin.Host is not ("127.0.0.1" or "localhost")||admin.Port!=5432)
            throw new InvalidOperationException("PROOFING_TEST_ROLES_CI_ONLY");
        var writerSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var readerSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        const string grants="""
            DO $proof_roles$
            BEGIN
              IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='taranom_sprint164_proof_writer_test') THEN
                CREATE ROLE taranom_sprint164_proof_writer_test LOGIN
                  NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
              END IF;
              IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='taranom_sprint164_proof_reader_test') THEN
                CREATE ROLE taranom_sprint164_proof_reader_test LOGIN
                  NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
              END IF;
            END;
            $proof_roles$;
            REVOKE ALL ON TABLE internal_test_proofing_case,internal_test_proofing_event,
                internal_test_proofing_audit,internal_test_proofing_denial FROM PUBLIC;
            REVOKE ALL ON TABLE internal_test_proofing_case,internal_test_proofing_event,
                internal_test_proofing_audit,internal_test_proofing_denial
                FROM taranom_sprint164_proof_writer_test,taranom_sprint164_proof_reader_test;
            REVOKE ALL ON SCHEMA public
                FROM taranom_sprint164_proof_writer_test,taranom_sprint164_proof_reader_test;
            GRANT USAGE ON SCHEMA public
                TO taranom_sprint164_proof_writer_test,taranom_sprint164_proof_reader_test;
            GRANT SELECT,INSERT,UPDATE ON internal_test_proofing_case
                TO taranom_sprint164_proof_writer_test;
            GRANT SELECT,INSERT ON internal_test_proofing_event,internal_test_proofing_audit
                TO taranom_sprint164_proof_writer_test;
            GRANT INSERT ON internal_test_proofing_denial TO taranom_sprint164_proof_writer_test;
            GRANT SELECT ON internal_test_proofing_case,internal_test_proofing_event,
                internal_test_proofing_audit TO taranom_sprint164_proof_reader_test;
            """;
        await using(var cn=await owner.OpenConnectionAsync())
        {
            await using(var cmd=new NpgsqlCommand(grants,cn))
                await cmd.ExecuteNonQueryAsync();
            // These values are independently generated uppercase hex, no user input.
            await using var pwd=new NpgsqlCommand(
                "ALTER ROLE "+WriterRole+" LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE "+
                "NOREPLICATION PASSWORD '"+writerSecret+"';"+
                "ALTER ROLE "+ReaderRole+" LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE "+
                "NOREPLICATION PASSWORD '"+readerSecret+"';",cn);
            await pwd.ExecuteNonQueryAsync();
        }
        NpgsqlDataSource? write=null,read=null;
        try
        {
            var wc=new NpgsqlConnectionStringBuilder(admin.ConnectionString)
                {Username=WriterRole,Password=writerSecret,Pooling=false};
            var rc=new NpgsqlConnectionStringBuilder(admin.ConnectionString)
                {Username=ReaderRole,Password=readerSecret,Pooling=false};
            write=NpgsqlDataSource.Create(wc.ConnectionString);
            read=NpgsqlDataSource.Create(rc.ConnectionString);
            await VerifyRestrictedRoleAsync(write,WriterRole,true);
            await VerifyRestrictedRoleAsync(read,ReaderRole,false);
            return (write,read);
        }
        catch
        {
            if(write is not null)await write.DisposeAsync();
            if(read is not null)await read.DisposeAsync();
            throw;
        }
    }

    private static async Task VerifyRestrictedRoleAsync(
        NpgsqlDataSource source,string role,bool writer)
    {
        await using var cn=await source.OpenConnectionAsync();
        const string sql="""
            SELECT current_user,session_user,rolsuper,rolcreatedb,rolcreaterole,
                rolreplication,rolinherit,has_schema_privilege(current_user,'public','CREATE'),
                has_table_privilege(current_user,'internal_test_proofing_case','SELECT'),
                has_table_privilege(current_user,'internal_test_proofing_case','INSERT'),
                has_table_privilege(current_user,'internal_test_proofing_case','UPDATE'),
                has_table_privilege(current_user,'internal_test_proofing_case','DELETE'),
                has_table_privilege(current_user,'internal_test_proofing_event','SELECT'),
                has_table_privilege(current_user,'internal_test_proofing_event','INSERT'),
                has_table_privilege(current_user,'internal_test_proofing_event','UPDATE'),
                has_table_privilege(current_user,'internal_test_proofing_event','DELETE'),
                has_table_privilege(current_user,'internal_test_proofing_audit','SELECT'),
                has_table_privilege(current_user,'internal_test_proofing_audit','INSERT'),
                has_table_privilege(current_user,'internal_test_proofing_audit','UPDATE'),
                has_table_privilege(current_user,'internal_test_proofing_audit','DELETE'),
                has_table_privilege(current_user,'internal_test_proofing_denial','SELECT'),
                has_table_privilege(current_user,'internal_test_proofing_denial','INSERT'),
                has_table_privilege(current_user,'internal_test_proofing_denial','UPDATE'),
                has_table_privilege(current_user,'internal_test_proofing_denial','DELETE'),
                has_table_privilege(current_user,'internal_test_care_case','SELECT'),
                has_table_privilege(current_user,'internal_test_support_case','SELECT'),
                has_table_privilege(current_user,'internal_test_late_evidence_case','SELECT')
            FROM pg_roles WHERE rolname=current_user
            """;
        await using(var cmd=new NpgsqlCommand(sql,cn))
        await using(var row=await cmd.ExecuteReaderAsync())
        {
            if(!await row.ReadAsync()||row.GetString(0)!=role||row.GetString(1)!=role||
                Enumerable.Range(2,6).Any(i=>row.GetBoolean(i))||
                !row.GetBoolean(8)||row.GetBoolean(9)!=writer||
                row.GetBoolean(10)!=writer||row.GetBoolean(11)||
                !row.GetBoolean(12)||row.GetBoolean(13)!=writer||
                row.GetBoolean(14)||row.GetBoolean(15)||
                !row.GetBoolean(16)||row.GetBoolean(17)!=writer||
                row.GetBoolean(18)||row.GetBoolean(19)||row.GetBoolean(20)||
                row.GetBoolean(21)!=writer||row.GetBoolean(22)||row.GetBoolean(23)||
                row.GetBoolean(24)||row.GetBoolean(25)||row.GetBoolean(26))
                throw new InvalidOperationException("PROOFING_DB_ROLE_PRIVILEGES_UNVERIFIABLE");
        }
        // Test actual negative SQL under each principal, not only metadata.
        await DenyOperationAsync(cn,writer?
            "DELETE FROM internal_test_proofing_event WHERE false":
            "UPDATE internal_test_proofing_case SET case_revision=0 WHERE false");
        await DenyOperationAsync(cn,"SELECT case_ref FROM internal_test_care_case LIMIT 1");
        await DenyOperationAsync(cn,"SELECT case_ref FROM internal_test_support_case LIMIT 1");
        await DenyOperationAsync(cn,"SELECT case_ref FROM internal_test_late_evidence_case LIMIT 1");
        await DenyOperationAsync(cn,"SELECT attempt_ref FROM internal_test_proofing_denial LIMIT 1");
    }

    private static async Task DenyOperationAsync(NpgsqlConnection cn,string sql)
    {
        try
        {
            await using var cmd=new NpgsqlCommand(sql,cn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch(PostgresException ex) when(ex.SqlState=="42501"){return;}
        throw new InvalidOperationException("PROOFING_FORBIDDEN_SQL_ACCEPTED");
    }

    private static void CheckSyntheticCase(string caseRef)
    {
        if (string.IsNullOrWhiteSpace(caseRef) || caseRef.Length > 100 ||
            !caseRef.StartsWith("test:", StringComparison.Ordinal) ||
            caseRef.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != ':' && ch != '-' && ch != '_'))
            throw new InvalidOperationException("TEST_PROOFING_CASE_REF_REQUIRED");
    }

    internal static async Task InitializeIsolatedTestSchemaAsync(
        NpgsqlDataSource schemaOwner, CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS internal_test_proofing_case (
                case_ref text PRIMARY KEY,
                case_revision bigint NOT NULL DEFAULT 0 CHECK (case_revision >= 0),
                identity_revision bigint NOT NULL DEFAULT 0 CHECK (identity_revision >= 0),
                review_code integer NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS internal_test_proofing_event (
                case_ref text NOT NULL REFERENCES internal_test_proofing_case(case_ref),
                case_revision bigint NOT NULL CHECK (case_revision > 0),
                identity_revision bigint NOT NULL CHECK (identity_revision >= 0),
                request_ref uuid NOT NULL,
                command_json text NOT NULL,
                review_code integer NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY(case_ref,case_revision),
                UNIQUE(case_ref,request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_proofing_audit (
                case_ref text NOT NULL,
                case_revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                event_digest text NOT NULL,
                occurred_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(case_ref,case_revision),
                FOREIGN KEY(case_ref,case_revision)
                    REFERENCES internal_test_proofing_event(case_ref,case_revision)
            );
            CREATE TABLE IF NOT EXISTS internal_test_proofing_denial (
                attempt_ref uuid PRIMARY KEY,
                case_ref text NOT NULL,
                request_ref uuid NOT NULL,
                reason_code integer NOT NULL CHECK (reason_code IN (3, 4, 5, 6, 7)),
                observed_case_revision bigint NOT NULL CHECK (observed_case_revision >= 0),
                occurred_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE OR REPLACE FUNCTION internal_test_proofing_immutable()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'TEST_PROOFING_APPEND_ONLY'
                    USING ERRCODE = 'P0001';
            END;
            $$;
            CREATE OR REPLACE FUNCTION internal_test_proofing_audit_guard()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW.case_ref LIKE 'test:abort-%' THEN
                        RAISE EXCEPTION 'TEST_PROOFING_AUDIT_UNAVAILABLE'
                            USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END IF;
                RAISE EXCEPTION 'TEST_PROOFING_APPEND_ONLY'
                    USING ERRCODE = 'P0001';
            END;
            $$;
            CREATE OR REPLACE FUNCTION internal_test_proofing_denial_guard()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW.case_ref LIKE 'test:abort-deny-%' THEN
                        RAISE EXCEPTION 'TEST_PROOFING_DENIAL_AUDIT_UNAVAILABLE'
                            USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END IF;
                RAISE EXCEPTION 'TEST_PROOFING_APPEND_ONLY'
                    USING ERRCODE = 'P0001';
            END;
            $$;
            DROP TRIGGER IF EXISTS internal_test_proofing_denial_immutable
                ON internal_test_proofing_denial;
            CREATE TRIGGER internal_test_proofing_denial_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON internal_test_proofing_denial
                FOR EACH ROW EXECUTE FUNCTION internal_test_proofing_denial_guard();
            DROP TRIGGER IF EXISTS internal_test_proofing_event_immutable
                ON internal_test_proofing_event;
            CREATE TRIGGER internal_test_proofing_event_immutable
                BEFORE UPDATE OR DELETE ON internal_test_proofing_event
                FOR EACH ROW EXECUTE FUNCTION internal_test_proofing_immutable();
            DROP TRIGGER IF EXISTS internal_test_proofing_audit_immutable
                ON internal_test_proofing_audit;
            CREATE TRIGGER internal_test_proofing_audit_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON internal_test_proofing_audit
                FOR EACH ROW EXECUTE FUNCTION internal_test_proofing_audit_guard();
            """;
        await using var connection = await schemaOwner.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    // INSERT + row lock serializes all writers to a synthetic case across
    // replicas. The audit, event and state must commit in the SAME transaction.
    // No state/event is committed when idempotency or audit insertion fails.
    internal async Task<ProofingCaseChange> ApplyAsync(
        string caseRef, ProofingCaseCommand input, CancellationToken ct = default)
    {
        CheckSyntheticCase(caseRef);
        await using var connection = await writer.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var init = new NpgsqlCommand("""
            INSERT INTO internal_test_proofing_case (case_ref) VALUES (@case)
            ON CONFLICT DO NOTHING
            """, connection, tx))
        {
            init.Parameters.AddWithValue("case", caseRef);
            await init.ExecuteNonQueryAsync(ct);
        }

        var current = await RehydrateLockedAsync(connection, tx, caseRef, ct);
        var change = ProofingCaseJournal.Apply(current, input);
        if (change.Outcome != ProofingJournalOutcome.Applied)
        {
            // Idempotent historical replay is not a rejected operation.
            // A failed command must not commit the synthetic case row
            // created above, even when no prior case existed. Roll it back
            // BEFORE inserting the separate, minimal denial audit.
            await tx.RollbackAsync(ct);
            if (change.Outcome != ProofingJournalOutcome.Replayed)
                await RecordDeniedAttemptAsync(connection, caseRef, input.RequestId,
                    change.Outcome, current.CaseRevision, ct);
            // Denial-audit failure throws instead of returning an unlogged deny.
            return change;
        }

        var snapshot = change.Snapshot;
        var json = JsonSerializer.Serialize(input);
        var digest = Digest(caseRef, snapshot.CaseRevision,
            snapshot.IdentityRevision, json, snapshot.Review);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO internal_test_proofing_event
              (case_ref,case_revision,identity_revision,request_ref,
               command_json,review_code,event_digest)
            VALUES (@case,@revision,@identity,@request,@json,@review,@digest)
            """, connection, tx))
        {
            insert.Parameters.AddWithValue("case", caseRef);
            insert.Parameters.AddWithValue("revision", snapshot.CaseRevision);
            insert.Parameters.AddWithValue("identity", snapshot.IdentityRevision);
            insert.Parameters.AddWithValue("request", input.RequestId);
            insert.Parameters.AddWithValue("json", json);
            insert.Parameters.AddWithValue("review", (int)snapshot.Review);
            insert.Parameters.AddWithValue("digest", digest);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_proofing_audit
                (case_ref,case_revision,request_ref,event_digest)
            VALUES (@case,@revision,@request,@digest)
            """, connection, tx))
        {
            audit.Parameters.AddWithValue("case", caseRef);
            audit.Parameters.AddWithValue("revision", snapshot.CaseRevision);
            audit.Parameters.AddWithValue("request", input.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_proofing_case
            SET case_revision=@revision,identity_revision=@identity,review_code=@review
            WHERE case_ref=@case AND case_revision=@expected
            """, connection, tx))
        {
            update.Parameters.AddWithValue("case", caseRef);
            update.Parameters.AddWithValue("expected", current.CaseRevision);
            update.Parameters.AddWithValue("revision", snapshot.CaseRevision);
            update.Parameters.AddWithValue("identity", snapshot.IdentityRevision);
            update.Parameters.AddWithValue("review", (int)snapshot.Review);
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("TEST_PROOFING_CAS_FAILED_CLOSED");
        }

        await tx.CommitAsync(ct);
        return change;
    }

    // Deliberately omit the original payload, principal, national identifier,
    // tenant and provider assertion. This is not a complete operational audit.
    // The only auditable outcomes here are deterministic reducer denials.
    // Invalid case references and database corruption fail before this point.
    private static async Task RecordDeniedAttemptAsync(
        NpgsqlConnection connection, string caseRef, Guid requestRef,
        ProofingJournalOutcome reason, long observedRevision, CancellationToken ct)
    {
        if (reason is not (ProofingJournalOutcome.InvalidCommand
            or ProofingJournalOutcome.ConflictingReplay
            or ProofingJournalOutcome.StaleCaseRevision
            or ProofingJournalOutcome.StaleIdentityRevision
            or ProofingJournalOutcome.NotApplicable))
            throw new InvalidOperationException("TEST_PROOFING_DENIAL_REASON_INVALID");

        await using var command = new NpgsqlCommand("""
            INSERT INTO internal_test_proofing_denial
                (attempt_ref,case_ref,request_ref,reason_code,observed_case_revision)
            VALUES (@attempt,@case,@request,@reason,@revision)
            """, connection);
        command.Parameters.AddWithValue("attempt", Guid.NewGuid());
        command.Parameters.AddWithValue("case", caseRef);
        command.Parameters.AddWithValue("request", requestRef);
        command.Parameters.AddWithValue("reason", (int)reason);
        command.Parameters.AddWithValue("revision", observedRevision);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task<ProofingCaseSnapshot> ReadAsync(
        string caseRef, CancellationToken ct = default)
    {
        CheckSyntheticCase(caseRef);
        await using var connection = await reader.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead,ct);
        await using(var ro=new NpgsqlCommand("SET TRANSACTION READ ONLY",connection,tx))
            await ro.ExecuteNonQueryAsync(ct);
        var current = await RehydrateLockedAsync(connection, tx, caseRef, ct,
            allowMissing: true, lockCaseRow: false);
        await tx.CommitAsync(ct);
        return current;
    }

    // Replay every committed event and verify its paired audit, digest and
    // projected current state. This detects silent history/state divergence.
    private static async Task<ProofingCaseSnapshot> RehydrateLockedAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, string caseRef,
        CancellationToken ct, bool allowMissing = false, bool lockCaseRow = true)
    {
        long expectedRevision, expectedIdentity;
        int expectedReview;
        await using (var state = new NpgsqlCommand(lockCaseRow ? """
            SELECT case_revision,identity_revision,review_code
            FROM internal_test_proofing_case WHERE case_ref=@case FOR UPDATE
            """ : """
            SELECT case_revision,identity_revision,review_code
            FROM internal_test_proofing_case WHERE case_ref=@case
            """, connection, tx))
        {
            state.Parameters.AddWithValue("case", caseRef);
            await using var reader = await state.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                if (allowMissing) return ProofingCaseSnapshot.Empty;
                throw new InvalidOperationException("TEST_PROOFING_STATE_MISSING");
            }
            expectedRevision = reader.GetInt64(0);
            expectedIdentity = reader.GetInt64(1);
            expectedReview = reader.GetInt32(2);
        }

        var result = ProofingCaseSnapshot.Empty;
        await using (var history = new NpgsqlCommand("""
            SELECT e.case_revision,e.identity_revision,e.request_ref,e.command_json,
                   e.review_code,e.event_digest,a.request_ref,a.event_digest
            FROM internal_test_proofing_event e
            LEFT JOIN internal_test_proofing_audit a
                 ON a.case_ref=e.case_ref AND a.case_revision=e.case_revision
            WHERE e.case_ref=@case ORDER BY e.case_revision
            """, connection, tx))
        {
            history.Parameters.AddWithValue("case", caseRef);
            await using var reader = await history.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(6) || reader.IsDBNull(7))
                    throw new InvalidOperationException("TEST_PROOFING_JOURNAL_CORRUPT");

                ProofingCaseCommand command;
                try
                {
                    command = JsonSerializer.Deserialize<ProofingCaseCommand>(reader.GetString(3));
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("TEST_PROOFING_JOURNAL_CORRUPT");
                }
                var applied = ProofingCaseJournal.Apply(result, command);
                var next = applied.Snapshot;
                if (applied.Outcome != ProofingJournalOutcome.Applied ||
                    next.CaseRevision != reader.GetInt64(0) ||
                    next.IdentityRevision != reader.GetInt64(1) ||
                    next.Review != (ProofingReviewState)reader.GetInt32(4) ||
                    command.RequestId != reader.GetGuid(2) ||
                    command.RequestId != reader.GetGuid(6) ||
                    reader.GetString(5) != reader.GetString(7) ||
                    reader.GetString(5) != Digest(caseRef, next.CaseRevision,
                        next.IdentityRevision, reader.GetString(3), next.Review))
                    throw new InvalidOperationException("TEST_PROOFING_JOURNAL_CORRUPT");
                result = next;
            }
        }
        if (result.CaseRevision != expectedRevision ||
            result.IdentityRevision != expectedIdentity ||
            (int)result.Review != expectedReview)
            throw new InvalidOperationException("TEST_PROOFING_JOURNAL_CORRUPT");
        return result;
    }

    private static string Digest(string caseRef, long revision, long identity,
        string json, ProofingReviewState review)
    {
        var canonical = string.Join("|", "taranom.test-proofing.v1", caseRef,
            revision.ToString(CultureInfo.InvariantCulture),
            identity.ToString(CultureInfo.InvariantCulture),
            ((int)review).ToString(CultureInfo.InvariantCulture), json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
