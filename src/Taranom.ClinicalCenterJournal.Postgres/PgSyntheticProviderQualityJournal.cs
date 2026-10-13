using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.ClinicalCenterGovernance;

namespace Taranom.ClinicalCenterJournal.Postgres;

// TRN-TECH-119: A strictly synthetic and non-deployable evidence ledger for
// *claims* of provider-quality oversight. No professional approval,
// provider registration, delegated legal authority or real incident delivery.
internal sealed class PgSyntheticProviderQualityJournal(NpgsqlDataSource source)
{
    private const string Corrupt = "TEST_PROVIDER_QUALITY_SOURCE_UNVERIFIABLE";

    internal static async Task InitializeIsolatedTestSchemaAsync(
        NpgsqlDataSource owner, CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_provider_quality_case (
                case_ref text PRIMARY KEY,
                current_revision bigint NOT NULL DEFAULT 0 CHECK(current_revision>=0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_provider_quality_event (
                case_ref text NOT NULL REFERENCES internal_test_provider_quality_case(case_ref),
                revision bigint NOT NULL CHECK(revision>0),
                request_ref uuid NOT NULL,
                action_code integer NOT NULL CHECK(action_code IN (1,2,3,4)),
                command_json text NOT NULL,
                event_digest text NOT NULL,
                PRIMARY KEY(case_ref,revision), UNIQUE(case_ref,request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_provider_quality_audit (
                case_ref text NOT NULL,
                revision bigint NOT NULL,
                request_ref uuid NOT NULL,
                event_digest text NOT NULL,
                observed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY(case_ref,revision),
                FOREIGN KEY(case_ref,revision)
                    REFERENCES internal_test_provider_quality_event(case_ref,revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_provider_quality_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'TEST_PROVIDER_QUALITY_APPEND_ONLY' USING ERRCODE='P0001';
            END $body$;
            DROP TRIGGER IF EXISTS internal_test_provider_quality_event_guard
                ON internal_test_provider_quality_event;
            CREATE TRIGGER internal_test_provider_quality_event_guard
                BEFORE UPDATE OR DELETE ON internal_test_provider_quality_event
                FOR EACH ROW EXECUTE FUNCTION internal_test_provider_quality_guard();
            DROP TRIGGER IF EXISTS internal_test_provider_quality_audit_guard
                ON internal_test_provider_quality_audit;
            CREATE TRIGGER internal_test_provider_quality_audit_guard
                BEFORE UPDATE OR DELETE ON internal_test_provider_quality_audit
                FOR EACH ROW EXECUTE FUNCTION internal_test_provider_quality_guard();
            """;
        await using var cn = await owner.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(ddl, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<SyntheticProviderQualityChange> ApplyAsync(
        string caseRef, SyntheticProviderQualityNote note, CancellationToken ct = default)
    {
        // Invalid input never creates a case header. The canonical reducer
        // handles ALL subsequent scope, replay and CAS decisions.
        if(!ValidCase(caseRef) || note.CaseRef != caseRef)
            return new(SyntheticProviderQualityNoteOutcome.Invalid,
                SyntheticProviderQualitySnapshot.Empty);
        var initial = SyntheticProviderQualityTrail.Apply(
            SyntheticProviderQualitySnapshot.Empty, note);
        if(initial.Outcome == SyntheticProviderQualityNoteOutcome.Invalid ||
           initial.Outcome == SyntheticProviderQualityNoteOutcome.NetworkScopeDenied)
            return initial;

        await using var cn = await source.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using(var create = new NpgsqlCommand("""
            INSERT INTO internal_test_provider_quality_case(case_ref) VALUES (@case)
            ON CONFLICT DO NOTHING
            """, cn, tx))
        {
            create.Parameters.AddWithValue("case", caseRef);
            await create.ExecuteNonQueryAsync(ct);
        }
        var current = await RebuildAsync(cn, tx, caseRef, true, ct);
        var changed = SyntheticProviderQualityTrail.Apply(current, note);
        if(changed.Outcome != SyntheticProviderQualityNoteOutcome.RecordedUnverified)
        {
            await tx.RollbackAsync(ct);
            return changed;
        }
        var revision = changed.Snapshot.Revision;
        var canonical = JsonSerializer.Serialize(note);
        var digest = Digest(caseRef, revision, note.RequestId, note.Action, canonical);

        await using(var ev = new NpgsqlCommand("""
            INSERT INTO internal_test_provider_quality_event
              (case_ref,revision,request_ref,action_code,command_json,event_digest)
            VALUES(@case,@revision,@request,@action,@json,@digest)
            """, cn, tx))
        {
            ev.Parameters.AddWithValue("case", caseRef);
            ev.Parameters.AddWithValue("revision", revision);
            ev.Parameters.AddWithValue("request", note.RequestId);
            ev.Parameters.AddWithValue("action", (int)note.Action);
            ev.Parameters.AddWithValue("json", canonical);
            ev.Parameters.AddWithValue("digest", digest);
            await ev.ExecuteNonQueryAsync(ct);
        }
        await using(var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_provider_quality_audit
              (case_ref,revision,request_ref,event_digest)
            VALUES(@case,@revision,@request,@digest)
            """, cn, tx))
        {
            audit.Parameters.AddWithValue("case", caseRef);
            audit.Parameters.AddWithValue("revision", revision);
            audit.Parameters.AddWithValue("request", note.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using(var update = new NpgsqlCommand("""
            UPDATE internal_test_provider_quality_case SET current_revision=@next
            WHERE case_ref=@case AND current_revision=@expected
            """, cn, tx))
        {
            update.Parameters.AddWithValue("case", caseRef);
            update.Parameters.AddWithValue("next", revision);
            update.Parameters.AddWithValue("expected", current.Revision);
            if(await update.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException(Corrupt);
        }
        await tx.CommitAsync(ct);
        return changed;
    }

    internal async Task<SyntheticProviderQualitySnapshot> ReadAsync(
        string caseRef, CancellationToken ct = default)
    {
        if(!ValidCase(caseRef))
            throw new InvalidOperationException("TEST_PROVIDER_QUALITY_CASE_REQUIRED");
        await using var cn = await source.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using(var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", cn, tx))
            await ro.ExecuteNonQueryAsync(ct);
        var result = await RebuildAsync(cn, tx, caseRef, false, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    internal async Task<SyntheticProviderQualityRead?> ReadUnverifiedCountsAsync(
        string caseRef, CancellationToken ct = default) =>
        SyntheticProviderQualityProjection.Project(await ReadAsync(caseRef, ct));

    private static async Task<SyntheticProviderQualitySnapshot> RebuildAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, string caseRef,
        bool writer, CancellationToken ct)
    {
        long expected = 0;
        await using(var head = new NpgsqlCommand(writer ? """
            SELECT current_revision FROM internal_test_provider_quality_case
            WHERE case_ref=@case FOR UPDATE
            """ : """
            SELECT current_revision FROM internal_test_provider_quality_case
            WHERE case_ref=@case
            """, cn, tx))
        {
            head.Parameters.AddWithValue("case", caseRef);
            var obj = await head.ExecuteScalarAsync(ct);
            if(obj is null)
            {
                if(writer) throw new InvalidOperationException(Corrupt);
            }
            else expected = (long)obj;
        }

        var current = SyntheticProviderQualitySnapshot.Empty;
        await using(var query = new NpgsqlCommand("""
            SELECT e.revision,e.request_ref,e.action_code,e.command_json,e.event_digest,
                   a.request_ref,a.event_digest
            FROM internal_test_provider_quality_event e
            LEFT JOIN internal_test_provider_quality_audit a
              ON a.case_ref=e.case_ref AND a.revision=e.revision
            WHERE e.case_ref=@case ORDER BY e.revision
            """, cn, tx))
        {
            query.Parameters.AddWithValue("case", caseRef);
            await using var rows = await query.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct))
            {
                if(rows.IsDBNull(5) || rows.IsDBNull(6))
                    throw new InvalidOperationException(Corrupt);
                var json = rows.GetString(3);
                SyntheticProviderQualityNote note;
                try { note = JsonSerializer.Deserialize<SyntheticProviderQualityNote>(json); }
                catch(JsonException) { throw new InvalidOperationException(Corrupt); }
                var version = rows.GetInt64(0);
                var id = rows.GetGuid(1);
                var action = (SyntheticProviderQualityNoteAction)rows.GetInt32(2);
                var digest = rows.GetString(4);
                var replay = SyntheticProviderQualityTrail.Apply(current, note);
                if(replay.Outcome != SyntheticProviderQualityNoteOutcome.RecordedUnverified ||
                   replay.Snapshot.Revision != version ||
                   note.CaseRef != caseRef ||
                   note.RequestId != id || note.Action != action ||
                   rows.GetGuid(5) != id || rows.GetString(6) != digest ||
                   digest != Digest(caseRef, version, id, action, json))
                    throw new InvalidOperationException(Corrupt);
                current = replay.Snapshot;
            }
        }
        if(current.Revision != expected) throw new InvalidOperationException(Corrupt);
        return current;
    }

    private static bool ValidCase(string? refValue) =>
        refValue is { Length: > 5 and <= 128 } &&
        refValue.StartsWith("test:", StringComparison.Ordinal) &&
        refValue.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is ':' or '-' or '_');

    private static string Digest(string caseRef, long rev, Guid requestId,
        SyntheticProviderQualityNoteAction action, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.synthetic-provider-quality.v1", caseRef,
            rev.ToString(CultureInfo.InvariantCulture), requestId.ToString("N"),
            ((int)action).ToString(CultureInfo.InvariantCulture), json))));
}
