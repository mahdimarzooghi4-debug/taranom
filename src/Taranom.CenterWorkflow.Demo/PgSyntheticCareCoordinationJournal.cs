using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// Synthetic-only, local PostgreSQL event source. No appointments, clinical
// orders, verified provider identity or actual member/patient data.
internal sealed class PgSyntheticCareCoordinationJournal(NpgsqlDataSource writer,NpgsqlDataSource reader)
{
    // TRN-TECH-172: isolated synthetic PostgreSQL principals, never schema owner.
    private const string WriterRole="taranom_sprint163_care_writer_test";
    private const string ReaderRole="taranom_sprint163_care_reader_test";

    internal static async Task<(NpgsqlDataSource Writer,NpgsqlDataSource Reader)>
        RestrictedSourcesAsync(NpgsqlDataSource owner,NpgsqlConnectionStringBuilder admin)
    {
        if(Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY")!="CI_EPHEMERAL_ONLY"||
           admin.Database!="taranom_sprint64_center"||
           admin.Host is not ("localhost" or "127.0.0.1")||admin.Port!=5432)
            throw new InvalidOperationException("CARE_ROLES_CI_ONLY");
        var writerSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var readerSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        const string sql="""
            DO $roles$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='taranom_sprint163_care_writer_test') THEN
                CREATE ROLE taranom_sprint163_care_writer_test LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
              END IF;
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='taranom_sprint163_care_reader_test') THEN
                CREATE ROLE taranom_sprint163_care_reader_test LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
              END IF;
            END;
            $roles$;
            REVOKE ALL ON TABLE internal_test_care_case,internal_test_care_event,
                internal_test_care_audit FROM PUBLIC;
            REVOKE ALL ON TABLE internal_test_care_case,internal_test_care_event,
                internal_test_care_audit FROM taranom_sprint163_care_writer_test,
                taranom_sprint163_care_reader_test;
            REVOKE ALL ON SCHEMA public FROM taranom_sprint163_care_writer_test,
                taranom_sprint163_care_reader_test;
            GRANT USAGE ON SCHEMA public TO taranom_sprint163_care_writer_test,
                taranom_sprint163_care_reader_test;
            GRANT SELECT,INSERT,UPDATE ON internal_test_care_case TO taranom_sprint163_care_writer_test;
            GRANT SELECT,INSERT ON internal_test_care_event,internal_test_care_audit
                TO taranom_sprint163_care_writer_test;
            GRANT SELECT ON internal_test_care_case,internal_test_care_event,internal_test_care_audit
                TO taranom_sprint163_care_reader_test;
            """;
        await using(var cn=await owner.OpenConnectionAsync())
        {
            await using(var cmd=new NpgsqlCommand(sql,cn)) await cmd.ExecuteNonQueryAsync();
            // Both credentials are fresh random uppercase hex, never user text.
            await using var pwd=new NpgsqlCommand(
                "ALTER ROLE "+WriterRole+" LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '"+writerSecret+"';"+
                "ALTER ROLE "+ReaderRole+" LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '"+readerSecret+"';",cn);
            await pwd.ExecuteNonQueryAsync();
        }
        NpgsqlDataSource? writer=null,reader=null;
        try
        {
            var w=new NpgsqlConnectionStringBuilder(admin.ConnectionString)
                {Username=WriterRole,Password=writerSecret,Pooling=false};
            var r=new NpgsqlConnectionStringBuilder(admin.ConnectionString)
                {Username=ReaderRole,Password=readerSecret,Pooling=false};
            writer=NpgsqlDataSource.Create(w.ConnectionString);
            reader=NpgsqlDataSource.Create(r.ConnectionString);
            await AttestAsync(writer,WriterRole,true);
            await AttestAsync(reader,ReaderRole,false);
            return (writer,reader);
        }
        catch
        {
            if(writer is not null)await writer.DisposeAsync();
            if(reader is not null)await reader.DisposeAsync();
            throw;
        }
    }

    private static async Task AttestAsync(NpgsqlDataSource source,string role,bool write)
    {
        await using var cn=await source.OpenConnectionAsync();
        const string sql="""
            SELECT current_user,session_user,rolsuper,rolcreatedb,rolcreaterole,
                rolreplication,rolinherit,has_schema_privilege(current_user,'public','CREATE'),
                has_table_privilege(current_user,'internal_test_care_case','SELECT'),
                has_table_privilege(current_user,'internal_test_care_case','INSERT'),
                has_table_privilege(current_user,'internal_test_care_case','UPDATE'),
                has_table_privilege(current_user,'internal_test_care_case','DELETE'),
                has_table_privilege(current_user,'internal_test_care_event','SELECT'),
                has_table_privilege(current_user,'internal_test_care_event','INSERT'),
                has_table_privilege(current_user,'internal_test_care_event','UPDATE'),
                has_table_privilege(current_user,'internal_test_care_event','DELETE'),
                has_table_privilege(current_user,'internal_test_care_audit','SELECT'),
                has_table_privilege(current_user,'internal_test_care_audit','INSERT'),
                has_table_privilege(current_user,'internal_test_care_audit','UPDATE'),
                has_table_privilege(current_user,'internal_test_care_audit','DELETE'),
                has_table_privilege(current_user,'internal_test_support_case','SELECT'),
                has_table_privilege(current_user,'internal_test_proofing_case','SELECT')
            FROM pg_roles WHERE rolname=current_user
            """;
        await using(var cmd=new NpgsqlCommand(sql,cn))
        await using(var row=await cmd.ExecuteReaderAsync())
        {
            if(!await row.ReadAsync()||row.GetString(0)!=role||row.GetString(1)!=role||
               Enumerable.Range(2,6).Any(i=>row.GetBoolean(i))||
               !row.GetBoolean(8)||row.GetBoolean(9)!=write||
               row.GetBoolean(10)!=write||row.GetBoolean(11)||
               !row.GetBoolean(12)||row.GetBoolean(13)!=write||
               row.GetBoolean(14)||row.GetBoolean(15)||
               !row.GetBoolean(16)||row.GetBoolean(17)!=write||
               row.GetBoolean(18)||row.GetBoolean(19)||
               row.GetBoolean(20)||row.GetBoolean(21))
                throw new InvalidOperationException("CARE_DATABASE_ROLE_PRIVILEGES_UNVERIFIABLE");
        }
        // Test real denied SQL, not just grants-table metadata.
        await DeniedAsync(cn,write?
            "DELETE FROM internal_test_care_event WHERE false":
            "UPDATE internal_test_care_case SET current_revision=0 WHERE false");
        await DeniedAsync(cn,"SELECT case_ref FROM internal_test_support_case LIMIT 1");
        await DeniedAsync(cn,"SELECT case_ref FROM internal_test_proofing_case LIMIT 1");
    }

    private static async Task DeniedAsync(NpgsqlConnection cn,string sql)
    {
        try
        {
            await using var cmd=new NpgsqlCommand(sql,cn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch(PostgresException ex)when(ex.SqlState=="42501"){return;}
        throw new InvalidOperationException("CARE_FORBIDDEN_SQL_ACCEPTED");
    }

    internal static async Task InitializeAsync(NpgsqlDataSource owner)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_care_case (
                case_ref text PRIMARY KEY,
                current_revision bigint NOT NULL DEFAULT 0 CHECK (current_revision>=0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_care_event (
                case_ref text NOT NULL REFERENCES internal_test_care_case(case_ref),
                revision bigint NOT NULL CHECK(revision>0),
                request_ref uuid NOT NULL,
                action_code integer NOT NULL CHECK(action_code IN (1,2,3)),
                command_json text NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY(case_ref,revision),UNIQUE(case_ref,request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_care_audit (
                case_ref text NOT NULL,
                revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                event_digest text NOT NULL,
                observed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(case_ref,revision),
                FOREIGN KEY(case_ref,revision)
                  REFERENCES internal_test_care_event(case_ref,revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_care_append_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              RAISE EXCEPTION 'TEST_CARE_APPEND_ONLY' USING ERRCODE='P0001';
            END $body$;
            DROP TRIGGER IF EXISTS internal_test_care_event_append_guard
              ON internal_test_care_event;
            CREATE TRIGGER internal_test_care_event_append_guard
            BEFORE UPDATE OR DELETE ON internal_test_care_event
            FOR EACH ROW EXECUTE FUNCTION internal_test_care_append_guard();
            DROP TRIGGER IF EXISTS internal_test_care_audit_append_guard
              ON internal_test_care_audit;
            CREATE TRIGGER internal_test_care_audit_append_guard
            BEFORE UPDATE OR DELETE ON internal_test_care_audit
            FOR EACH ROW EXECUTE FUNCTION internal_test_care_append_guard();
            """;
        await using var cn = await owner.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(ddl, cn);
        await cmd.ExecuteNonQueryAsync();
    }
    internal async Task<SyntheticCareTrailChange> ApplyAsync(SyntheticCareTrailCommand input)
    {
        var invalid = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty,input);
        if(invalid.Outcome==SyntheticCareTrailOutcome.Invalid)return invalid;
        await using var cn = await writer.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using(var create=new NpgsqlCommand("""
            INSERT INTO internal_test_care_case(case_ref) VALUES(@case)
            ON CONFLICT DO NOTHING
            """,cn,tx))
        {
            create.Parameters.AddWithValue("case",input.CaseRef);
            await create.ExecuteNonQueryAsync();
        }
        var snapshot=await RebuildAsync(cn,tx,input.CaseRef,true);
        var change=SyntheticCareCoordinationTrail.Apply(snapshot,input);
        if(change.Outcome!=SyntheticCareTrailOutcome.RecordedUnverified)
        {
            await tx.RollbackAsync();
            return change;
        }
        var revision=change.Snapshot.Revision;
        var json=JsonSerializer.Serialize(input);
        var digest=Digest(input.CaseRef,revision,input.RequestId,input.Action,json);
        await using(var append=new NpgsqlCommand("""
            INSERT INTO internal_test_care_event
              (case_ref,revision,request_ref,action_code,command_json,event_digest)
            VALUES(@case,@rev,@request,@action,@json,@digest)
            """,cn,tx))
        {
            append.Parameters.AddWithValue("case",input.CaseRef);
            append.Parameters.AddWithValue("rev",revision);
            append.Parameters.AddWithValue("request",input.RequestId);
            append.Parameters.AddWithValue("action",(int)input.Action);
            append.Parameters.AddWithValue("json",json);
            append.Parameters.AddWithValue("digest",digest);
            await append.ExecuteNonQueryAsync();
        }
        await using(var audit=new NpgsqlCommand("""
            INSERT INTO internal_test_care_audit
              (case_ref,revision,request_ref,event_digest)
            VALUES(@case,@rev,@request,@digest)
            """,cn,tx))
        {
            audit.Parameters.AddWithValue("case",input.CaseRef);
            audit.Parameters.AddWithValue("rev",revision);
            audit.Parameters.AddWithValue("request",input.RequestId);
            audit.Parameters.AddWithValue("digest",digest);
            await audit.ExecuteNonQueryAsync();
        }
        await using(var update=new NpgsqlCommand("""
            UPDATE internal_test_care_case SET current_revision=@next
            WHERE case_ref=@case AND current_revision=@before
            """,cn,tx))
        {
            update.Parameters.AddWithValue("next",revision);
            update.Parameters.AddWithValue("case",input.CaseRef);
            update.Parameters.AddWithValue("before",snapshot.Revision);
            if(await update.ExecuteNonQueryAsync()!=1)
                throw new InvalidOperationException("TEST_CARE_REVISION_CAS_FAILED");
        }
        await tx.CommitAsync();
        return change;
    }
    // TRN-TECH-186: bounded, explicit, source-verified synthetic case worklist.
    // List uses ONLY the reader principal and the canonical event/audit replay.
    // No raw JSON topic/medical inference, member IDs, actors, notes or authority.
    internal async Task<((
        string CaseRef,long Revision,int ContactClaims,int SourceClaims
        )[] Rows,bool HasMore)> ReadWorklistAsync(
            string? afterCaseRef,int limit,string claimFilter="ALL")
    {
        if(claimFilter is not ("ALL" or "HAS_CONTACT" or "HAS_SOURCE" or "HAS_BOTH")||
           limit is <1 or >10||(afterCaseRef is not null&&!Synthetic(afterCaseRef)))
            throw new InvalidOperationException("INVALID_TEST_CARE_CURSOR");
        await using var cn=await reader.OpenConnectionAsync();
        await using var tx=await cn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        await using(var ro=new NpgsqlCommand("SET TRANSACTION READ ONLY",cn,tx))
            await ro.ExecuteNonQueryAsync();
        var result=new List<(string CaseRef,long Revision,int ContactClaims,int SourceClaims)>();
        await using(var query=new NpgsqlCommand("""
            SELECT case_ref FROM internal_test_care_case
            WHERE current_revision>0
              AND (@after='' OR case_ref COLLATE "C" > @after COLLATE "C")
            ORDER BY case_ref COLLATE "C"
            LIMIT @max
            """,cn,tx))
        {
            query.Parameters.AddWithValue("after",afterCaseRef??string.Empty);
            // A filtered page must search past nonmatching cases BEFORE
            // applying its keyset page limit, not filter a raw N+1 slice.
            // The fixed scan cap fails closed instead of hiding later matches.
            query.Parameters.AddWithValue("max",claimFilter=="ALL"?limit+1:512);
            var refs=new List<string>();
            await using(var rows=await query.ExecuteReaderAsync())
            {
                while(await rows.ReadAsync())refs.Add(rows.GetString(0));
            }
            // Verify *every* candidate, including the limit+1 lookahead;
            // corrupt source cannot disappear merely because it is off page.
            foreach(var caseRef in refs)
            {
                if(!Synthetic(caseRef))
                    throw new InvalidOperationException("TEST_CARE_UNVERIFIED_REFERENCE");
                var state=await RebuildAsync(cn,tx,caseRef,false);
                if(state.Revision<1||state.History.Length!=state.Revision||
                   state.History[0].Command.Action!=
                       SyntheticCareTrailAction.RequestNonclinicalCoordination)
                    throw new InvalidOperationException("TEST_CARE_WORKLIST_UNVERIFIABLE");
                var contacts=state.History.Count(x=>x.Command.Action==
                    SyntheticCareTrailAction.RecordUnverifiedCoordinatorContact);
                var sources=state.History.Count(x=>x.Command.Action==
                    SyntheticCareTrailAction.AttachUnverifiedClinicalSourceReference);
                if(contacts+sources!=state.Revision-1)
                    throw new InvalidOperationException("TEST_CARE_WORKLIST_INVALID_HISTORY");
                var matches=claimFilter switch
                {
                    "ALL"=>true,
                    "HAS_CONTACT"=>contacts>0,
                    "HAS_SOURCE"=>sources>0,
                    "HAS_BOTH"=>contacts>0&&sources>0,
                    _=>false
                };
                if(matches)result.Add((caseRef,state.Revision,contacts,sources));
                if(result.Count>limit)break;
            }
            // If scanning exhausted a bounded candidate window without
            // finding N+1 matches, its next cursor is unknowable. Deny.
            if(claimFilter!="ALL"&&refs.Count==512&&result.Count<=limit)
                throw new InvalidOperationException("TEST_CARE_FILTER_SCAN_INCOMPLETE");
        }
        await tx.CommitAsync();
        return (result.Take(limit).ToArray(),result.Count>limit);
    }

    internal async Task<SyntheticCareTrailSnapshot> ReadAsync(string caseRef)
    {
        if(!Synthetic(caseRef))throw new InvalidOperationException("INVALID_CARE_CASE");
        await using var cn=await reader.OpenConnectionAsync();
        await using var tx=await cn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        await using(var ro=new NpgsqlCommand("SET TRANSACTION READ ONLY",cn,tx))
            await ro.ExecuteNonQueryAsync();
        var state=await RebuildAsync(cn,tx,caseRef,false);
        await tx.CommitAsync();
        return state;
    }
    private static async Task<SyntheticCareTrailSnapshot> RebuildAsync(
        NpgsqlConnection cn,NpgsqlTransaction tx,string caseRef,bool writer)
    {
        long expected;
        await using(var head=new NpgsqlCommand(writer ? """
            SELECT current_revision FROM internal_test_care_case
            WHERE case_ref=@case FOR UPDATE
            """ : """
            SELECT current_revision FROM internal_test_care_case
            WHERE case_ref=@case
            """,cn,tx))
        {
            head.Parameters.AddWithValue("case",caseRef);
            var result=await head.ExecuteScalarAsync();
            if(result is null)
            {
                if(writer)throw new InvalidOperationException("TEST_CARE_MISSING_HEADER");
                return SyntheticCareTrailSnapshot.Empty;
            }
            expected=(long)result;
        }
        var rebuilt=SyntheticCareTrailSnapshot.Empty;
        await using(var query=new NpgsqlCommand("""
            SELECT e.revision,e.request_ref,e.action_code,e.command_json,e.event_digest,
                   a.request_ref,a.event_digest
            FROM internal_test_care_event e
            LEFT JOIN internal_test_care_audit a
              ON a.case_ref=e.case_ref AND a.revision=e.revision
            WHERE e.case_ref=@case ORDER BY e.revision
            """,cn,tx))
        {
            query.Parameters.AddWithValue("case",caseRef);
            await using var rd=await query.ExecuteReaderAsync();
            while(await rd.ReadAsync())
            {
                if(rd.IsDBNull(5)||rd.IsDBNull(6))
                    throw new InvalidOperationException("TEST_CARE_AUDIT_MISSING");
                var json=rd.GetString(3);
                SyntheticCareTrailCommand input;
                try{input=JsonSerializer.Deserialize<SyntheticCareTrailCommand>(json);}
                catch(JsonException){throw new InvalidOperationException("TEST_CARE_CORRUPT");}
                var revision=rd.GetInt64(0);
                var req=rd.GetGuid(1);
                var action=(SyntheticCareTrailAction)rd.GetInt32(2);
                var digest=rd.GetString(4);
                var next=SyntheticCareCoordinationTrail.Apply(rebuilt,input);
                if(next.Outcome!=SyntheticCareTrailOutcome.RecordedUnverified||
                   next.Snapshot.Revision!=revision||input.CaseRef!=caseRef||
                   input.RequestId!=req||input.Action!=action||
                   rd.GetGuid(5)!=req||rd.GetString(6)!=digest||
                   digest!=Digest(caseRef,revision,req,action,json))
                    throw new InvalidOperationException("TEST_CARE_CORRUPT");
                rebuilt=next.Snapshot;
            }
        }
        if(expected!=rebuilt.Revision)throw new InvalidOperationException("TEST_CARE_REVISION_MISMATCH");
        return rebuilt;
    }
    private static bool Synthetic(string? s)=>s is {Length:>5 and <=128}&&
        s.StartsWith("test:",StringComparison.Ordinal)&&
        s.AsSpan(5).ToArray().All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or ':');
    private static string Digest(string caseRef,long revision,Guid request,
        SyntheticCareTrailAction action,string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.synthetic-care-journal.v1",caseRef,
            revision.ToString(CultureInfo.InvariantCulture),request.ToString("N"),
            ((int)action).ToString(CultureInfo.InvariantCulture),json))));
}
