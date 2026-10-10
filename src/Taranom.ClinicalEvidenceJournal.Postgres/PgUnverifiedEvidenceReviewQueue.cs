using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taranom.ClinicalEvidenceIntake;
using IntakeDomain = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Internal synthetic-only READ MODEL for unverified submissions. This does
// not authenticate a reviewer or allow real clinical/center data access.
internal readonly record struct ReviewQueueCursor(
    string CenterRef,
    EvidenceSubjectKind? SubjectFilter,
    string SubjectRef,
    EvidenceSubjectKind SubjectKind,
    long Revision);

internal readonly record struct UnverifiedReviewQueueItem(
    string CenterRef,
    string SubjectRef,
    EvidenceSubjectKind SubjectKind,
    long Revision,
    EvidenceKind EvidenceKind,
    string OpaqueEvidenceRef,
    string SubmittingActorRef);

internal sealed record UnverifiedReviewQueuePage(
    IReadOnlyList<UnverifiedReviewQueueItem> Items,
    ReviewQueueCursor? NextCursor);

// Test-only, caller-supplied centerRef is a filter, NOT an authority grant.
// No API, browser or operational database can invoke this as a live service.
internal sealed class PgUnverifiedEvidenceReviewQueue(NpgsqlDataSource dataSource)
{
    internal async Task<UnverifiedReviewQueuePage> ListAsync(
        string centerRef, EvidenceSubjectKind? subjectFilter, int pageSize,
        ReviewQueueCursor? cursor = null, CancellationToken ct = default)
    {
        if (!IntakeDomain.IsSyntheticRef(centerRef) ||
            (subjectFilter.HasValue && !Enum.IsDefined(subjectFilter.Value)) ||
            pageSize is < 1 or > 100)
            throw new ArgumentException("Invalid synthetic queue query.");

        if (cursor is { } c &&
            (!string.Equals(c.CenterRef, centerRef, StringComparison.Ordinal) ||
             c.SubjectFilter != subjectFilter ||
             !IntakeDomain.IsSyntheticRef(c.SubjectRef) ||
             !Enum.IsDefined(c.SubjectKind) ||
             c.Revision < 1))
            throw new ArgumentException("Cursor is not valid for this scoped query.");

        var sql = """
            SELECT e.subject_ref,e.subject_kind,e.event_revision,
                   e.request_ref,e.command_json,e.event_digest,
                   w.evidence_kind,w.claimant_ref,w.opaque_evidence_ref,
                   a.request_ref,a.event_digest,c.current_revision
            FROM internal_test_review_event e
            JOIN internal_test_review_case c
              ON c.center_ref=e.center_ref AND c.subject_ref=e.subject_ref
              AND c.subject_kind=e.subject_kind
            LEFT JOIN internal_test_review_work_item w
              ON w.center_ref=e.center_ref AND w.subject_ref=e.subject_ref
              AND w.subject_kind=e.subject_kind AND w.event_revision=e.event_revision
            LEFT JOIN internal_test_review_audit a
              ON a.center_ref=e.center_ref AND a.subject_ref=e.subject_ref
              AND a.subject_kind=e.subject_kind AND a.event_revision=e.event_revision
            WHERE e.center_ref=@center
            """;
        if (subjectFilter.HasValue) sql += " AND e.subject_kind=@filter";
        if (cursor is not null)
            sql += """
                 AND (e.subject_ref COLLATE "C",e.subject_kind,e.event_revision)
                     > (@after_subject COLLATE "C",@after_kind,@after_revision)
                """;
        sql += """
             ORDER BY e.subject_ref COLLATE "C",e.subject_kind,e.event_revision
             LIMIT @take
            """;

        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, cn);
        command.Parameters.AddWithValue("center", centerRef);
        if (subjectFilter.HasValue)
            command.Parameters.AddWithValue("filter", (int)subjectFilter.Value);
        if (cursor is { } after)
        {
            command.Parameters.AddWithValue("after_subject", after.SubjectRef);
            command.Parameters.AddWithValue("after_kind", (int)after.SubjectKind);
            command.Parameters.AddWithValue("after_revision", after.Revision);
        }
        command.Parameters.AddWithValue("take", pageSize + 1);

        var results = new List<UnverifiedReviewQueueItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (Enumerable.Range(6, 5).Any(reader.IsDBNull))
                throw new InvalidOperationException("TEST_REVIEW_QUEUE_INTEGRITY_FAILED");

            results.Add(ValidateRow(reader, centerRef));
        }

        var more = results.Count > pageSize;
        if (more) results.RemoveAt(results.Count - 1);
        ReviewQueueCursor? next = null;
        if (more)
        {
            var last = results[^1];
            next = new ReviewQueueCursor(centerRef, subjectFilter, last.SubjectRef,
                last.SubjectKind, last.Revision);
        }

        return new UnverifiedReviewQueuePage(results.AsReadOnly(), next);
    }

    // Shared validation for both live cursor pages and one-transaction frozen snapshots.
    internal static UnverifiedReviewQueueItem ValidateRow(NpgsqlDataReader reader, string centerRef)
    {
        var subject = reader.GetString(0);
        var kind = (EvidenceSubjectKind)reader.GetInt32(1);
        var revision = reader.GetInt64(2);
        var requestId = reader.GetGuid(3);
        var json = reader.GetString(4);
        var digest = reader.GetString(5);

        EvidenceSubmission input;
        try
        {
            input = JsonSerializer.Deserialize<EvidenceSubmission>(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("TEST_REVIEW_QUEUE_INTEGRITY_FAILED");
        }

        // Each returned queue item must match its accepted immutable
        // event, review-work row and paired audit. This per-page check is
        // NOT a full historical verification of records outside the page.
        if (!IntakeDomain.IsSyntheticRef(subject) ||
            !Enum.IsDefined(kind) ||
            revision is < 1 ||
            revision > reader.GetInt64(11) ||
            input.RequestId != requestId ||
            input.ExpectedRevision != revision - 1 ||
            input.CenterRef != centerRef ||
            input.SubjectRef != subject ||
            input.SubjectKind != kind ||
            input.EvidenceKind != (EvidenceKind)reader.GetInt32(6) ||
            input.SubmittingActorRef != reader.GetString(7) ||
            input.OpaqueEvidenceRef != reader.GetString(8) ||
            requestId != reader.GetGuid(9) ||
            digest != reader.GetString(10) ||
            digest != Digest(centerRef, subject, kind, revision, json))
            throw new InvalidOperationException("TEST_REVIEW_QUEUE_INTEGRITY_FAILED");

        return new UnverifiedReviewQueueItem(
            centerRef, subject, kind, revision, input.EvidenceKind,
            input.OpaqueEvidenceRef!, input.SubmittingActorRef!);
    }

    private static string Digest(string center, string subject, EvidenceSubjectKind kind,
        long revision, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            "taranom.test-review-intake.v1", center, subject,
            ((int)kind).ToString(CultureInfo.InvariantCulture),
            revision.ToString(CultureInfo.InvariantCulture), json))));
}
