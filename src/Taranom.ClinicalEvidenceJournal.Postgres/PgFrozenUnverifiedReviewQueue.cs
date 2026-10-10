using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using IntakeDomain = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Synthetic-only frozen membership of pending, UNVERIFIED claims. The snapshot
// is a transactionally captured read model, NOT reviewer authorization.
internal readonly record struct FrozenReviewCursor(
    Guid SnapshotId, string CenterRef, EvidenceSubjectKind? SubjectFilter, int NextIndex);

internal sealed record FrozenReviewPage(
    Guid SnapshotId, IReadOnlyList<UnverifiedReviewQueueItem> Items,
    FrozenReviewCursor? NextCursor);

internal sealed class PgFrozenUnverifiedReviewQueue(NpgsqlDataSource source)
{
    internal static async Task InitializeTestSchemaAsync(
        NpgsqlDataSource owner, CancellationToken ct = default)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS internal_test_frozen_review_snapshot (
                snapshot_id uuid PRIMARY KEY,
                center_ref text NOT NULL,
                subject_filter integer NULL CHECK (subject_filter IN (1, 2)),
                item_count integer NOT NULL CHECK(item_count >= 0),
                manifest_json text NOT NULL,
                manifest_digest text NOT NULL,
                recorded_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE OR REPLACE FUNCTION internal_test_frozen_review_guard()
            RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
              RAISE EXCEPTION 'TEST_FROZEN_REVIEW_APPEND_ONLY' USING ERRCODE='P0001';
            END; $body$;
            DROP TRIGGER IF EXISTS internal_test_frozen_review_guard_tr
              ON internal_test_frozen_review_snapshot;
            CREATE TRIGGER internal_test_frozen_review_guard_tr
              BEFORE UPDATE OR DELETE ON internal_test_frozen_review_snapshot
              FOR EACH ROW EXECUTE FUNCTION internal_test_frozen_review_guard();
            """;
        await using var cn = await owner.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(ddl, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<Guid> CaptureAsync(string center,
        EvidenceSubjectKind? subjectFilter, CancellationToken ct = default)
    {
        ValidateQuery(center, subjectFilter, 1);
        await using var cn = await source.OpenConnectionAsync(ct);
        // A single database transaction and MVCC snapshot defines the full
        // membership. Future inserts, including smaller sort keys, are excluded.
        await using var tx = await cn.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, ct);
        var query = """
            SELECT e.subject_ref,e.subject_kind,e.event_revision,
                   e.request_ref,e.command_json,e.event_digest,
                   w.evidence_kind,w.claimant_ref,w.opaque_evidence_ref,
                   a.request_ref,a.event_digest,c.current_revision
            FROM internal_test_review_event e
            JOIN internal_test_review_case c
              ON c.center_ref=e.center_ref AND c.subject_ref=e.subject_ref
              AND c.subject_kind=e.subject_kind
            LEFT JOIN internal_test_review_work_item w ON
                w.center_ref=e.center_ref AND w.subject_ref=e.subject_ref
                AND w.subject_kind=e.subject_kind AND w.event_revision=e.event_revision
            LEFT JOIN internal_test_review_audit a ON
                a.center_ref=e.center_ref AND a.subject_ref=e.subject_ref
                AND a.subject_kind=e.subject_kind AND a.event_revision=e.event_revision
            WHERE e.center_ref=@center
            """;
        if (subjectFilter.HasValue) query += " AND e.subject_kind=@filter";
        query += """
             ORDER BY e.subject_ref COLLATE "C",e.subject_kind,e.event_revision
            """;
        var items = new List<UnverifiedReviewQueueItem>();
        await using (var cmd = new NpgsqlCommand(query, cn, tx))
        {
            cmd.Parameters.AddWithValue("center", center);
            if (subjectFilter.HasValue)
                cmd.Parameters.AddWithValue("filter", (int)subjectFilter.Value);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                items.Add(PgUnverifiedEvidenceReviewQueue.ValidateRow(reader, center));
        }

        var id = Guid.NewGuid();
        var json = JsonSerializer.Serialize(items);
        var digest = Digest(id, center, subjectFilter, json);
        await using (var save = new NpgsqlCommand("""
            INSERT INTO internal_test_frozen_review_snapshot
              (snapshot_id,center_ref,subject_filter,item_count,manifest_json,manifest_digest)
            VALUES (@id,@center,@filter,@count,@json,@digest)
            """, cn, tx))
        {
            save.Parameters.AddWithValue("id", id);
            save.Parameters.AddWithValue("center", center);
            save.Parameters.AddWithValue("filter", subjectFilter.HasValue
                ? (object)(int)subjectFilter.Value : DBNull.Value);
            save.Parameters.AddWithValue("count", items.Count);
            save.Parameters.AddWithValue("json", json);
            save.Parameters.AddWithValue("digest", digest);
            await save.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return id;
    }

    internal async Task<FrozenReviewPage> PageAsync(string center,
        EvidenceSubjectKind? subjectFilter, Guid snapshotId,
        int pageSize, FrozenReviewCursor? cursor = null,
        CancellationToken ct = default)
    {
        ValidateQuery(center, subjectFilter, pageSize);
        if (snapshotId == Guid.Empty ||
            (cursor is { } token &&
              (token.SnapshotId != snapshotId ||
               !string.Equals(token.CenterRef, center, StringComparison.Ordinal) ||
               token.SubjectFilter != subjectFilter || token.NextIndex < 1)))
            throw new ArgumentException("Invalid synthetic snapshot cursor.");

        await using var cn = await source.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT center_ref,subject_filter,item_count,manifest_json,manifest_digest
            FROM internal_test_frozen_review_snapshot WHERE snapshot_id=@id
            """, cn);
        cmd.Parameters.AddWithValue("id", snapshotId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("TEST_FROZEN_REVIEW_NOT_FOUND");

        var savedCenter = reader.GetString(0);
        EvidenceSubjectKind? savedFilter = reader.IsDBNull(1)
            ? null : (EvidenceSubjectKind)reader.GetInt32(1);
        var count = reader.GetInt32(2);
        var json = reader.GetString(3);
        var digest = reader.GetString(4);

        if (!string.Equals(savedCenter, center, StringComparison.Ordinal) ||
            savedFilter != subjectFilter)
            throw new ArgumentException("Snapshot scope does not match query.");

        if (digest != Digest(snapshotId, center, subjectFilter, json))
            throw new InvalidOperationException("TEST_FROZEN_REVIEW_INTEGRITY_FAILED");
        List<UnverifiedReviewQueueItem>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<UnverifiedReviewQueueItem>>(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("TEST_FROZEN_REVIEW_INTEGRITY_FAILED");
        }
        if (items is null || items.Count != count ||
            items.Any(x => x.CenterRef != center ||
                !IntakeDomain.IsSyntheticRef(x.SubjectRef) ||
                !IntakeDomain.IsSyntheticRef(x.OpaqueEvidenceRef) ||
                !IntakeDomain.IsSyntheticRef(x.SubmittingActorRef) ||
                !Enum.IsDefined(x.SubjectKind) || !Enum.IsDefined(x.EvidenceKind) ||
                x.Revision < 1 ||
                (subjectFilter.HasValue && x.SubjectKind != subjectFilter.Value)) ||
            items.Select(x => (x.SubjectRef, x.SubjectKind, x.Revision)).Distinct().Count()
                != items.Count ||
            items.Zip(items.Skip(1)).Any(pair =>
                string.CompareOrdinal(pair.First.SubjectRef, pair.Second.SubjectRef) > 0 ||
                (pair.First.SubjectRef == pair.Second.SubjectRef &&
                    ((int)pair.First.SubjectKind > (int)pair.Second.SubjectKind ||
                    (pair.First.SubjectKind == pair.Second.SubjectKind &&
                        pair.First.Revision >= pair.Second.Revision)))))
            throw new InvalidOperationException("TEST_FROZEN_REVIEW_INTEGRITY_FAILED");

        var index = cursor?.NextIndex ?? 0;
        if (index > items.Count)
            throw new ArgumentException("Cursor position outside this snapshot.");

        var batch = items.Skip(index).Take(pageSize).ToArray();
        var next = index + batch.Length;
        FrozenReviewCursor? nextCursor = next < items.Count
            ? new(snapshotId, center, subjectFilter, next) : null;
        return new FrozenReviewPage(snapshotId, Array.AsReadOnly(batch), nextCursor);
    }

    private static void ValidateQuery(string center,
        EvidenceSubjectKind? filter, int size)
    {
        if (!IntakeDomain.IsSyntheticRef(center) ||
            (filter.HasValue && !Enum.IsDefined(filter.Value)) ||
            size is < 1 or > 100)
            throw new ArgumentException("Invalid synthetic frozen queue query.");
    }

    private static string Digest(Guid id, string center,
        EvidenceSubjectKind? filter, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-frozen-review.v1", id.ToString("N"), center,
            filter.HasValue
                ? ((int)filter.Value).ToString(CultureInfo.InvariantCulture) : "-",
            json))));
}
