using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.ProofingJournal.Postgres;

// TRN-TECH-118: internal TEST-ONLY append journal for alleged access events.
// Not a record-access API, identity provider, consent grant or clinical log.
internal sealed class PgSyntheticClinicalAccessTransparencyJournal(NpgsqlDataSource source)
{
    private const string Corrupt = "TEST_CLINICAL_ACCESS_HISTORY_UNVERIFIABLE";
    internal static async Task InitializeIsolatedTestSchemaAsync(
        NpgsqlDataSource owner,CancellationToken ct=default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_clinical_access_case (
                case_ref text PRIMARY KEY,
                current_revision bigint NOT NULL DEFAULT 0 CHECK(current_revision>=0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_clinical_access_event (
                case_ref text NOT NULL REFERENCES internal_test_clinical_access_case(case_ref),
                revision bigint NOT NULL CHECK(revision>0),
                request_ref uuid NOT NULL,
                action_code integer NOT NULL CHECK(action_code IN (1,2,3)),
                command_json text NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY(case_ref,revision),UNIQUE(case_ref,request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_clinical_access_audit (
                case_ref text NOT NULL, revision bigint NOT NULL,
                request_ref uuid NOT NULL, event_digest text NOT NULL,
                observed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(case_ref,revision),
                FOREIGN KEY(case_ref,revision)
                  REFERENCES internal_test_clinical_access_event(case_ref,revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_clinical_access_append_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'TEST_CLINICAL_ACCESS_APPEND_ONLY' USING ERRCODE='P0001';
            END $body$;
            DROP TRIGGER IF EXISTS internal_test_clinical_access_event_guard
              ON internal_test_clinical_access_event;
            CREATE TRIGGER internal_test_clinical_access_event_guard
              BEFORE UPDATE OR DELETE ON internal_test_clinical_access_event
              FOR EACH ROW EXECUTE FUNCTION internal_test_clinical_access_append_guard();
            DROP TRIGGER IF EXISTS internal_test_clinical_access_audit_guard
              ON internal_test_clinical_access_audit;
            CREATE TRIGGER internal_test_clinical_access_audit_guard
              BEFORE UPDATE OR DELETE ON internal_test_clinical_access_audit
              FOR EACH ROW EXECUTE FUNCTION internal_test_clinical_access_append_guard();
            """;
        await using var cn=await owner.OpenConnectionAsync(ct);
        await using var cmd=new NpgsqlCommand(ddl,cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<SyntheticAccessNoteChange> ApplyAsync(
        string caseRef,SyntheticAccessNoteCommand input,CancellationToken ct=default)
    {
        if(!Synthetic(caseRef))
            return new(SyntheticAccessNoteOutcome.Invalid,
                SyntheticAccessTransparencySnapshot.Empty);
        // Only an invalid structural command is rejected before the database.
        // The canonical reducer is the only arbiter of claimed follow-ups.
        var preliminary=SyntheticClinicalAccessTransparencyTrail.Apply(
            SyntheticAccessTransparencySnapshot.Empty,input);
        if(preliminary.Outcome==SyntheticAccessNoteOutcome.Invalid)
            return preliminary;
        await using var cn=await source.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await using(var ensure=new NpgsqlCommand("""
            INSERT INTO internal_test_clinical_access_case(case_ref)
            VALUES(@case) ON CONFLICT DO NOTHING
            """,cn,tx))
        {
            ensure.Parameters.AddWithValue("case",caseRef);
            await ensure.ExecuteNonQueryAsync(ct);
        }
        // Row lock protects against two different replicas both appending.
        var current=await RebuildAsync(cn,tx,caseRef,true,ct);
        var change=SyntheticClinicalAccessTransparencyTrail.Apply(current,input);
        if(change.Outcome!=SyntheticAccessNoteOutcome.RecordedUnverified)
        {
            await tx.RollbackAsync(ct);
            return change;
        }
        var revision=change.Snapshot.Revision;
        var json=JsonSerializer.Serialize(input);
        var digest=Digest(caseRef,revision,input.RequestId,input.Action,json);
        await using(var ev=new NpgsqlCommand("""
            INSERT INTO internal_test_clinical_access_event
                (case_ref,revision,request_ref,action_code,command_json,event_digest)
            VALUES(@case,@rev,@request,@action,@json,@digest)
            """,cn,tx))
        {
            ev.Parameters.AddWithValue("case",caseRef);
            ev.Parameters.AddWithValue("rev",revision);
            ev.Parameters.AddWithValue("request",input.RequestId);
            ev.Parameters.AddWithValue("action",(int)input.Action);
            ev.Parameters.AddWithValue("json",json);
            ev.Parameters.AddWithValue("digest",digest);
            await ev.ExecuteNonQueryAsync(ct);
        }
        await using(var audit=new NpgsqlCommand("""
            INSERT INTO internal_test_clinical_access_audit
                (case_ref,revision,request_ref,event_digest)
            VALUES(@case,@rev,@request,@digest)
            """,cn,tx))
        {
            audit.Parameters.AddWithValue("case",caseRef);
            audit.Parameters.AddWithValue("rev",revision);
            audit.Parameters.AddWithValue("request",input.RequestId);
            audit.Parameters.AddWithValue("digest",digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using(var update=new NpgsqlCommand("""
            UPDATE internal_test_clinical_access_case
            SET current_revision=@next
            WHERE case_ref=@case AND current_revision=@prior
            """,cn,tx))
        {
            update.Parameters.AddWithValue("next",revision);
            update.Parameters.AddWithValue("case",caseRef);
            update.Parameters.AddWithValue("prior",current.Revision);
            if(await update.ExecuteNonQueryAsync(ct)!=1)
                throw new InvalidOperationException(Corrupt);
        }
        await tx.CommitAsync(ct);
        return change;
    }

    // An internal technical source read, NEVER a public clinical record read.
    internal async Task<SyntheticAccessTransparencySnapshot> ReadAsync(
        string caseRef,CancellationToken ct=default)
    {
        if(!Synthetic(caseRef))throw new InvalidOperationException("TEST_CASE_REQUIRED");
        await using var cn=await source.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        await using(var ro=new NpgsqlCommand("SET TRANSACTION READ ONLY",cn,tx))
            await ro.ExecuteNonQueryAsync(ct);
        var result=await RebuildAsync(cn,tx,caseRef,false,ct);
        await tx.CommitAsync(ct);
        return result;
    }

    // Only the existing exact claimed-member projection may be returned.
    // Denied callers receive no lineage, counts, scope, document or actor.
    internal async Task<SyntheticAccessTransparencyRead> ProjectForClaimedMemberAsync(
        string caseRef,SelfReadBoundaryInput viewer,CancellationToken ct=default)
    {
        var state=await ReadAsync(caseRef,ct);
        return SyntheticClinicalAccessTransparencyTrail.ProjectForClaimedMember(state,viewer);
    }

    private static async Task<SyntheticAccessTransparencySnapshot> RebuildAsync(
        NpgsqlConnection cn,NpgsqlTransaction tx,string caseRef,bool writer,
        CancellationToken ct)
    {
        long expected=0;
        await using(var header=new NpgsqlCommand(writer?"""
            SELECT current_revision FROM internal_test_clinical_access_case
            WHERE case_ref=@case FOR UPDATE
            """:"""
            SELECT current_revision FROM internal_test_clinical_access_case
            WHERE case_ref=@case
            """,cn,tx))
        {
            header.Parameters.AddWithValue("case",caseRef);
            var value=await header.ExecuteScalarAsync(ct);
            if(value is null)
            {
                if(writer)throw new InvalidOperationException(Corrupt);
                // Check for orphaned events/audits on a missing header below.
            }
            else expected=(long)value;
        }
        var state=SyntheticAccessTransparencySnapshot.Empty;
        await using(var query=new NpgsqlCommand("""
            SELECT e.revision,e.request_ref,e.action_code,e.command_json,e.event_digest,
                   a.request_ref,a.event_digest
            FROM internal_test_clinical_access_event e
            LEFT JOIN internal_test_clinical_access_audit a
              ON a.case_ref=e.case_ref AND a.revision=e.revision
            WHERE e.case_ref=@case ORDER BY e.revision
            """,cn,tx))
        {
            query.Parameters.AddWithValue("case",caseRef);
            await using var rows=await query.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct))
            {
                if(rows.IsDBNull(5)||rows.IsDBNull(6))
                    throw new InvalidOperationException(Corrupt);
                var json=rows.GetString(3);
                SyntheticAccessNoteCommand item;
                try{item=JsonSerializer.Deserialize<SyntheticAccessNoteCommand>(json);}
                catch(JsonException){throw new InvalidOperationException(Corrupt);}
                var revision=rows.GetInt64(0);
                var id=rows.GetGuid(1);
                var kind=(SyntheticAccessNoteAction)rows.GetInt32(2);
                var checksum=rows.GetString(4);
                var next=SyntheticClinicalAccessTransparencyTrail.Apply(state,item);
                if(next.Outcome!=SyntheticAccessNoteOutcome.RecordedUnverified||
                   next.Snapshot.Revision!=revision||item.RequestId!=id||
                   item.Action!=kind||rows.GetGuid(5)!=id||
                   rows.GetString(6)!=checksum||
                   !string.Equals(checksum,Digest(caseRef,revision,id,kind,json),
                      StringComparison.Ordinal))
                    throw new InvalidOperationException(Corrupt);
                state=next.Snapshot;
            }
        }
        if(state.Revision!=expected)
            throw new InvalidOperationException(Corrupt);
        return state;
    }
    private static string Digest(string caseRef,long rev,Guid requestId,
        SyntheticAccessNoteAction action,string json)=>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-clinical-access-transparency.v1",
            caseRef,rev.ToString(CultureInfo.InvariantCulture),
            requestId.ToString("N"),((int)action).ToString(CultureInfo.InvariantCulture),
            json))));
    private static bool Synthetic(string? x)=>
        x is {Length:>5 and <=128}&&x.StartsWith("test:",StringComparison.Ordinal)&&
        x.AsSpan(5).ToArray().All(c=>char.IsAsciiLetterOrDigit(c)||c is ':' or '_' or '-');
}
