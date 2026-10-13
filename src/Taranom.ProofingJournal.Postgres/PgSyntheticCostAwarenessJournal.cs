using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.ProofingJournal.Postgres;

// Isolated synthetic PostgreSQL exercise only. No legal consent, payment,
// verified member identity, treatment authorisation or live data transport.
internal enum SyntheticCostRegistration
{
    Registered = 1,
    Replayed = 2,
    Invalid = 3,
    ConflictingSnapshot = 4
}

internal sealed class PgSyntheticCostAwarenessJournal(NpgsqlDataSource source)
{
    private const string Corrupt = "TEST_COST_AWARENESS_JOURNAL_CORRUPT";

    internal static async Task InitializeIsolatedTestSchemaAsync(
        NpgsqlDataSource owner, CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS internal_test_cost_preview (
              preview_ref uuid PRIMARY KEY,
              member_ref text NOT NULL,
              preview_revision bigint NOT NULL CHECK(preview_revision > 0),
              preview_fingerprint text NOT NULL,
              preview_json text NOT NULL,
              choice_revision bigint NOT NULL DEFAULT 0 CHECK(choice_revision >= 0)
            );
            CREATE TABLE IF NOT EXISTS internal_test_cost_choice_event (
              preview_ref uuid NOT NULL REFERENCES internal_test_cost_preview(preview_ref),
              choice_revision bigint NOT NULL CHECK(choice_revision > 0),
              request_ref uuid NOT NULL,
              command_json text NOT NULL,
              event_digest text NOT NULL,
              PRIMARY KEY(preview_ref,choice_revision),
              UNIQUE(preview_ref,request_ref)
            );
            CREATE TABLE IF NOT EXISTS internal_test_cost_choice_audit (
              preview_ref uuid NOT NULL,
              choice_revision bigint NOT NULL,
              request_ref uuid NOT NULL,
              event_digest text NOT NULL,
              recorded_at timestamptz NOT NULL DEFAULT now(),
              PRIMARY KEY(preview_ref,choice_revision),
              FOREIGN KEY(preview_ref,choice_revision) REFERENCES
                internal_test_cost_choice_event(preview_ref,choice_revision)
            );
            CREATE OR REPLACE FUNCTION internal_test_cost_preview_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              IF TG_OP = 'UPDATE' THEN
                IF NEW.preview_ref IS DISTINCT FROM OLD.preview_ref OR
                   NEW.member_ref IS DISTINCT FROM OLD.member_ref OR
                   NEW.preview_revision IS DISTINCT FROM OLD.preview_revision OR
                   NEW.preview_fingerprint IS DISTINCT FROM OLD.preview_fingerprint OR
                   NEW.preview_json IS DISTINCT FROM OLD.preview_json OR
                   NEW.choice_revision <> OLD.choice_revision + 1
                THEN
                  RAISE EXCEPTION 'TEST_COST_PREVIEW_IMMUTABLE'
                    USING ERRCODE='P0001';
                END IF;
                RETURN NEW;
              END IF;
              RAISE EXCEPTION 'TEST_COST_PREVIEW_IMMUTABLE' USING ERRCODE='P0001';
            END;
            $body$;
            CREATE OR REPLACE FUNCTION internal_test_cost_history_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              RAISE EXCEPTION 'TEST_COST_CHOICE_APPEND_ONLY' USING ERRCODE='P0001';
            END;
            $body$;
            CREATE OR REPLACE FUNCTION internal_test_cost_audit_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              IF TG_OP = 'INSERT' THEN
                IF EXISTS (
                  SELECT 1 FROM internal_test_cost_preview p
                  WHERE p.preview_ref=NEW.preview_ref
                    AND p.member_ref LIKE 'test:abort-audit-%'
                ) THEN
                  RAISE EXCEPTION 'TEST_COST_AUDIT_UNAVAILABLE'
                    USING ERRCODE='P0001';
                END IF;
                RETURN NEW;
              END IF;
              RAISE EXCEPTION 'TEST_COST_CHOICE_APPEND_ONLY' USING ERRCODE='P0001';
            END;
            $body$;
            DROP TRIGGER IF EXISTS internal_test_cost_preview_immutable
              ON internal_test_cost_preview;
            CREATE TRIGGER internal_test_cost_preview_immutable
              BEFORE UPDATE OR DELETE ON internal_test_cost_preview
              FOR EACH ROW EXECUTE FUNCTION internal_test_cost_preview_guard();
            DROP TRIGGER IF EXISTS internal_test_cost_event_immutable
              ON internal_test_cost_choice_event;
            CREATE TRIGGER internal_test_cost_event_immutable
              BEFORE UPDATE OR DELETE ON internal_test_cost_choice_event
              FOR EACH ROW EXECUTE FUNCTION internal_test_cost_history_guard();
            DROP TRIGGER IF EXISTS internal_test_cost_audit_immutable
              ON internal_test_cost_choice_audit;
            CREATE TRIGGER internal_test_cost_audit_immutable
              BEFORE INSERT OR UPDATE OR DELETE ON internal_test_cost_choice_audit
              FOR EACH ROW EXECUTE FUNCTION internal_test_cost_audit_guard();
            """;
        await using var cn = await owner.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, cn);
        await command.ExecuteNonQueryAsync(ct);
    }

    // Pinned input is not an authenticated quote. Registration cannot change
    // the prior source/component contents of this preview UUID, even with
    // a higher claimed revision. A genuinely different preview needs a new ID.
    internal async Task<SyntheticCostRegistration> RegisterAsync(
        SyntheticCostPreview supplied, CancellationToken ct = default)
    {
        var canonical = Canonical(supplied);
        if (canonical is null)
            return SyntheticCostRegistration.Invalid;

        var preview = canonical.Value;
        await using var cn = await source.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        int inserted;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO internal_test_cost_preview
              (preview_ref,member_ref,preview_revision,preview_fingerprint,preview_json)
            VALUES (@preview,@member,@revision,@fingerprint,@json)
            ON CONFLICT DO NOTHING
            """, cn, tx))
        {
            insert.Parameters.AddWithValue("preview", preview.PreviewId);
            insert.Parameters.AddWithValue("member", preview.MemberRef!);
            insert.Parameters.AddWithValue("revision", preview.PreviewRevision);
            insert.Parameters.AddWithValue("fingerprint", preview.Fingerprint!);
            insert.Parameters.AddWithValue("json", JsonSerializer.Serialize(preview.Lines));
            inserted = await insert.ExecuteNonQueryAsync(ct);
        }
        // Always verify stored snapshot and audit before returning replay.
        var stored = await ReadLockedAsync(cn, tx, preview.PreviewId, ct)
            ?? throw new InvalidOperationException(Corrupt);
        await tx.CommitAsync(ct);
        if (inserted == 1)
            return SyntheticCostRegistration.Registered;
        return stored.MemberRef == preview.MemberRef &&
               stored.PreviewRevision == preview.PreviewRevision &&
               stored.PreviewFingerprint == preview.Fingerprint
            ? SyntheticCostRegistration.Replayed
            : SyntheticCostRegistration.ConflictingSnapshot;
    }

    internal async Task<CostAwarenessChange> RecordAsync(
        CostAwarenessCommand input, CancellationToken ct = default)
    {
        if (input.PreviewId == Guid.Empty)
            return new(CostAwarenessOutcome.Invalid, null);
        await using var cn = await source.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var snapshot = await ReadLockedAsync(cn, tx, input.PreviewId, ct);
        var change = CostAwarenessRecorder.Record(snapshot, input);
        if (change.Outcome != CostAwarenessOutcome.RecordedUnverified)
        {
            await tx.RollbackAsync(ct);
            return change;
        }

        var revision = change.Snapshot!.Revision;
        var json = JsonSerializer.Serialize(input);
        var digest = Digest(input.PreviewId, revision, input.RequestId, json);
        await using (var append = new NpgsqlCommand("""
            INSERT INTO internal_test_cost_choice_event
              (preview_ref,choice_revision,request_ref,command_json,event_digest)
            VALUES (@preview,@revision,@request,@json,@digest)
            """, cn, tx))
        {
            append.Parameters.AddWithValue("preview", input.PreviewId);
            append.Parameters.AddWithValue("revision", revision);
            append.Parameters.AddWithValue("request", input.RequestId);
            append.Parameters.AddWithValue("json", json);
            append.Parameters.AddWithValue("digest", digest);
            await append.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO internal_test_cost_choice_audit
              (preview_ref,choice_revision,request_ref,event_digest)
            VALUES (@preview,@revision,@request,@digest)
            """, cn, tx))
        {
            audit.Parameters.AddWithValue("preview", input.PreviewId);
            audit.Parameters.AddWithValue("revision", revision);
            audit.Parameters.AddWithValue("request", input.RequestId);
            audit.Parameters.AddWithValue("digest", digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_cost_preview SET choice_revision=@next
            WHERE preview_ref=@preview AND choice_revision=@previous
            """, cn, tx))
        {
            update.Parameters.AddWithValue("next", revision);
            update.Parameters.AddWithValue("previous", snapshot!.Revision);
            update.Parameters.AddWithValue("preview", input.PreviewId);
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("TEST_COST_CHOICE_CAS_FAILED");
        }
        await tx.CommitAsync(ct);
        return change;
    }

    internal async Task<CostAwarenessSnapshot?> ReadAsync(
        Guid previewId, CancellationToken ct = default)
    {
        if (previewId == Guid.Empty)
            return null;
        await using var cn = await source.OpenConnectionAsync(ct);
        // TRN-TECH-111: header, ordered choices and paired audits MUST see
        // one committed snapshot, with no writer row lock on HTTP GET.
        await using var tx = await cn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, ct);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", cn, tx))
            await readOnly.ExecuteNonQueryAsync(ct);
        var snapshot = await ReadLockedAsync(cn, tx, previewId, ct,
            lockPreviewRow: false);
        await tx.CommitAsync(ct);
        return snapshot;
    }

    private static async Task<CostAwarenessSnapshot?> ReadLockedAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, Guid previewId,
        CancellationToken ct, bool lockPreviewRow = true)
    {
        string member;
        long previewRevision, choiceRevision;
        string fingerprint, json;
        // Mutating Register/Record callers retain FOR UPDATE and original
        // CAS. Only ReadAsync opts into a non-locking MVCC header SELECT.
        await using (var head = new NpgsqlCommand(lockPreviewRow ? """
            SELECT member_ref,preview_revision,preview_fingerprint,
                   preview_json,choice_revision
            FROM internal_test_cost_preview WHERE preview_ref=@preview FOR UPDATE
            """ : """
            SELECT member_ref,preview_revision,preview_fingerprint,
                   preview_json,choice_revision
            FROM internal_test_cost_preview WHERE preview_ref=@preview
            """, cn, tx))
        {
            head.Parameters.AddWithValue("preview", previewId);
            await using var row = await head.ExecuteReaderAsync(ct);
            if (!await row.ReadAsync(ct))
                return null;
            member = row.GetString(0);
            previewRevision = row.GetInt64(1);
            fingerprint = row.GetString(2);
            json = row.GetString(3);
            choiceRevision = row.GetInt64(4);
        }

        SyntheticCostLine[] lines;
        try
        {
            lines = JsonSerializer.Deserialize<SyntheticCostLine[]>(json)
                ?? throw new InvalidOperationException(Corrupt);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(Corrupt);
        }
        var preview = SyntheticCostPreviewBuilder.Build(
            member, previewId, previewRevision, lines);
        if (preview.State == SyntheticCostPreviewState.Invalid ||
            preview.Fingerprint != fingerprint)
            throw new InvalidOperationException(Corrupt);
        var snapshot = CostAwarenessSnapshot.FromPreview(preview)
            ?? throw new InvalidOperationException(Corrupt);

        await using (var history = new NpgsqlCommand("""
            SELECT e.choice_revision,e.request_ref,e.command_json,e.event_digest,
                   a.request_ref,a.event_digest
            FROM internal_test_cost_choice_event e
            LEFT JOIN internal_test_cost_choice_audit a
              ON a.preview_ref=e.preview_ref AND a.choice_revision=e.choice_revision
            WHERE e.preview_ref=@preview ORDER BY e.choice_revision
            """, cn, tx))
        {
            history.Parameters.AddWithValue("preview", previewId);
            await using var reader = await history.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(4) || reader.IsDBNull(5))
                    throw new InvalidOperationException(Corrupt);
                var commandJson = reader.GetString(2);
                CostAwarenessCommand input;
                try
                {
                    input = JsonSerializer.Deserialize<CostAwarenessCommand>(commandJson);
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException(Corrupt);
                }
                var result = CostAwarenessRecorder.Record(snapshot, input);
                var revision = reader.GetInt64(0);
                var request = reader.GetGuid(1);
                var digest = reader.GetString(3);
                if (result.Outcome != CostAwarenessOutcome.RecordedUnverified ||
                    result.Snapshot?.Revision != revision ||
                    input.PreviewId != previewId ||
                    input.RequestId != request ||
                    reader.GetGuid(4) != request ||
                    reader.GetString(5) != digest ||
                    digest != Digest(previewId, revision, request, commandJson))
                    throw new InvalidOperationException(Corrupt);
                snapshot = result.Snapshot;
            }
        }
        if (snapshot.Revision != choiceRevision)
            throw new InvalidOperationException(Corrupt);
        return snapshot;
    }

    private static SyntheticCostPreview? Canonical(SyntheticCostPreview supplied)
    {
        if (supplied.Lines.IsDefault) return null;
        var rebuilt = SyntheticCostPreviewBuilder.Build(supplied.MemberRef,
            supplied.PreviewId, supplied.PreviewRevision, supplied.Lines);
        return rebuilt.State != SyntheticCostPreviewState.Invalid &&
               rebuilt.State == supplied.State && rebuilt.Fingerprint == supplied.Fingerprint
            ? rebuilt : null;
    }

    private static string Digest(Guid preview, long revision, Guid request, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-cost-choice.v1", preview.ToString("N"),
            revision.ToString(CultureInfo.InvariantCulture),
            request.ToString("N"), json))));
}
