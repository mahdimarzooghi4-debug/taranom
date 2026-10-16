using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-147: genuine local PostgreSQL immutable DENIAL log.
// The owner connection is allowed only by the existing disposable test-host
// process. It MUST NOT be reused as a real administrative data source.
internal sealed record AiAdminDenialRecord(
    long Seq, string RequestRef, string Operation, string TargetRef,
    string EvidenceStatus, string PreviousSha256, string EventSha256);
internal sealed record AiAdminDenialWrite(string Outcome, AiAdminDenialRecord Record);

internal sealed class PgSyntheticAiAdminDenial(NpgsqlDataSource source)
{
    private const string EmptyDigest = "0000000000000000000000000000000000000000000000000000000000000000";
    private const int MaxEvents = 4096;
    private const string SelectSql = """
        SELECT seq,request_ref,operation,target_ref,evidence_status,
               previous_sha,event_sha
        FROM internal_test_ai_admin_denial ORDER BY seq
        """;

    internal static async Task InitializeAsync(NpgsqlDataSource source)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
                "CI_EPHEMERAL_ONLY" ||
            Environment.GetEnvironmentVariable("TARANOM_AI_ADMIN_QA") !=
                "CI_EPHEMERAL_ONLY")
            throw new InvalidOperationException("AI_AUDIT_ISOLATED_TEST_ONLY");
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_ai_admin_denial (
              seq bigint PRIMARY KEY CHECK(seq > 0),
              request_ref text NOT NULL UNIQUE,
              operation text NOT NULL,
              target_ref text NOT NULL,
              evidence_status text NOT NULL,
              previous_sha text NOT NULL CHECK(length(previous_sha)=64),
              event_sha text NOT NULL CHECK(length(event_sha)=64)
            );
            REVOKE ALL ON TABLE internal_test_ai_admin_denial FROM PUBLIC;
            CREATE OR REPLACE FUNCTION internal_test_ai_admin_denial_immutable()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION 'AI_ADMIN_DENIAL_APPEND_ONLY'
                USING ERRCODE='P0001';
            END;
            $$;
            REVOKE ALL ON FUNCTION internal_test_ai_admin_denial_immutable() FROM PUBLIC;
            DROP TRIGGER IF EXISTS internal_test_ai_admin_denial_guard
              ON internal_test_ai_admin_denial;
            CREATE TRIGGER internal_test_ai_admin_denial_guard
              BEFORE UPDATE OR DELETE ON internal_test_ai_admin_denial
              FOR EACH ROW EXECUTE FUNCTION internal_test_ai_admin_denial_immutable();
            """;
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Digest(long seq,string request,string operation,
        string target,string evidence,string previous)
    {
        var bytes=Encoding.UTF8.GetBytes(
            $"{seq}|{request}|{operation}|{target}|{evidence}|{previous}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static async Task<List<AiAdminDenialRecord>> ReadAndVerifyAsync(
        NpgsqlConnection conn, NpgsqlTransaction? transaction = null)
    {
        var records = new List<AiAdminDenialRecord>();
        await using var cmd = new NpgsqlCommand(SelectSql,conn,transaction);
        await using var reader=await cmd.ExecuteReaderAsync();
        string previous=EmptyDigest;
        long next=1;
        while (await reader.ReadAsync())
        {
            if (next > MaxEvents)
                throw new InvalidOperationException("TEST_AI_AUDIT_CAPACITY_REACHED");
            var record=new AiAdminDenialRecord(
                reader.GetInt64(0), reader.GetString(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetString(5),
                reader.GetString(6));
            if (record.Seq!=next || record.PreviousSha256!=previous ||
                record.EventSha256!=Digest(record.Seq,record.RequestRef,
                    record.Operation,record.TargetRef,record.EvidenceStatus,
                    record.PreviousSha256) ||
                record.EvidenceStatus is not (
                    "IDENTITY_NOT_VERIFIED_ACCESS_DENIED" or
                    "VERIFIED_IDENTITY_EVIDENCE_BUT_ACCESS_DENIED"))
                throw new InvalidOperationException("TEST_AI_AUDIT_INTEGRITY_FAILED");
            records.Add(record);
            previous=record.EventSha256;
            next++;
        }
        return records;
    }

    internal async Task<AiAdminDenialWrite> RecordAsync(string requestRef,
        string operation,string targetRef,string evidenceStatus)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        await using (var lockCmd=new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(138, 147)",conn,tx))
            await lockCmd.ExecuteNonQueryAsync();
        var history=await ReadAndVerifyAsync(conn,tx);
        var old=history.Find(x=>x.RequestRef==requestRef);
        if (old is not null)
        {
            if (old.Operation!=operation || old.TargetRef!=targetRef ||
                old.EvidenceStatus!=evidenceStatus)
                return new AiAdminDenialWrite("CONFLICTING_DENIAL_REPLAY",old);
            return new AiAdminDenialWrite("REPLAY_DENIED",old);
        }
        if (history.Count>=MaxEvents)
            throw new InvalidOperationException("TEST_AI_AUDIT_CAPACITY_REACHED");
        var last=history.Count==0?EmptyDigest:history[^1].EventSha256;
        var seq=history.Count+1L;
        var entry=new AiAdminDenialRecord(seq,requestRef,operation,targetRef,
            evidenceStatus,last,Digest(seq,requestRef,operation,targetRef,
                evidenceStatus,last));
        await using(var cmd=new NpgsqlCommand("""
            INSERT INTO internal_test_ai_admin_denial
              (seq,request_ref,operation,target_ref,evidence_status,previous_sha,event_sha)
            VALUES (@s,@r,@o,@t,@e,@p,@h)
            """,conn,tx))
        {
            cmd.Parameters.AddWithValue("s",entry.Seq);
            cmd.Parameters.AddWithValue("r",entry.RequestRef);
            cmd.Parameters.AddWithValue("o",entry.Operation);
            cmd.Parameters.AddWithValue("t",entry.TargetRef);
            cmd.Parameters.AddWithValue("e",entry.EvidenceStatus);
            cmd.Parameters.AddWithValue("p",entry.PreviousSha256);
            cmd.Parameters.AddWithValue("h",entry.EventSha256);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return new AiAdminDenialWrite("DENIAL_RECORDED",entry);
    }

    internal async Task<AiAdminDenialRecord?> ReadAsync(string requestRef)
    {
        await using var conn=await source.OpenConnectionAsync();
        var history=await ReadAndVerifyAsync(conn);
        return history.Find(x=>x.RequestRef==requestRef);
    }
}
