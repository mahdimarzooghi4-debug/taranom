using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// Synthetic-only, local PostgreSQL event source. No appointments, clinical
// orders, verified provider identity or actual member/patient data.
internal sealed class PgSyntheticCareCoordinationJournal(NpgsqlDataSource source)
{
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
        await using var cn = await source.OpenConnectionAsync();
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
    internal async Task<SyntheticCareTrailSnapshot> ReadAsync(string caseRef)
    {
        if(!Synthetic(caseRef))throw new InvalidOperationException("INVALID_CARE_CASE");
        await using var cn=await source.OpenConnectionAsync();
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
