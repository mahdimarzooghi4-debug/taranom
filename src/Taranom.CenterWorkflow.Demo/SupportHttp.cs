using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-075 — ONLY ephemeral test: reference-only, no member verification,
// no legally submitted complaint and no clinical/insurance decision.
internal static class SupportHttp
{
    internal static PgSyntheticSupportJournal Store { get; set; } = null!;
    internal static PgSyntheticSupportJournal ReadStore { get; set; } = null!;

    internal static async Task<IResult> ReadAsync(HttpContext context)
    {
        var caseRef = context.Request.RouteValues["caseRef"]?.ToString();
        if (!PgSyntheticSupportJournal.IsSynthetic(caseRef) ||
            context.Request.Query.Count != 0)
            return Denied(400, "INVALID_TEST_CASE");
        try
        {
            var snapshot = await ReadStore.ReadAsync(caseRef!);
            return Results.Json(ToResponse(snapshot, caseRef!, "READ"));
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            return Denied(503, "TEST_SUPPORT_JOURNAL_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> WriteAsync(HttpContext context)
    {
        var caseRef = context.Request.RouteValues["caseRef"]?.ToString();
        if (!PgSyntheticSupportJournal.IsSynthetic(caseRef) ||
            context.Request.Query.Count != 0)
            return Denied(400, "INVALID_TEST_CASE");
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin is not ("http://127.0.0.1:4320" or "http://localhost:4320"))
            return Denied(403, "ORIGIN_DENIED");
        if (context.Request.ContentType is null ||
            !context.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            context.Request.ContentLength is > 1400)
            return Denied(400, "INVALID_TEST_SUPPORT_COMMAND");
        try
        {
            var input = await ParseAsync(context.Request, caseRef!);
            if (input is null) return Denied(400, "INVALID_TEST_SUPPORT_COMMAND");
            var change = await Store.ApplyAsync(caseRef!, input.Value);
            var status = change.Outcome switch
            {
                SyntheticSupportOutcome.RecordedUnverified or SyntheticSupportOutcome.Replayed => 200,
                SyntheticSupportOutcome.ConflictingReplay or SyntheticSupportOutcome.StaleRevision
                    or SyntheticSupportOutcome.OriginalMissing or SyntheticSupportOutcome.ScopeMismatch => 409,
                _ => 400
            };
            return Results.Json(ToResponse(change.Snapshot, caseRef!, change.Outcome.ToString()),
                statusCode: status);
        }
        catch (OperationCanceledException)
        {
            return Denied(503, "TEST_SUPPORT_JOURNAL_UNVERIFIABLE");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            return Denied(503, "TEST_SUPPORT_JOURNAL_UNVERIFIABLE");
        }
    }

    internal static object ToResponse(SyntheticSupportSnapshot state, string caseRef, string outcome)
    {
        var read = SyntheticSupportReadProjection.Project(state);
        var first = state.History.IsEmpty ? (SyntheticSupportCommand?)null : state.History[0].Command;
        return new
        {
            contract = "TRN-TECH-075",
            syntheticOnly = true,
            memberAuthenticated = false,
            complaintLegallySubmitted = false,
            insurerContacted = false,
            clinicalReviewCompleted = false,
            nationalReleaseAllowed = false,
            caseRef,
            revision = state.Revision,
            outcome,
            topic = read?.Topic.ToString(),
            memberRef = read?.MemberRef,
            originalRequestId = first?.RequestId,
            evidenceRef = first?.UnverifiedEvidenceRef,
            claimedSubmitterRef = first?.ClaimedSubmitterRef,
            followUpCount = read?.UnverifiedNoteCount ?? 0,
            followUps = state.History.Skip(1).Select(x => new
            {
                revision = x.Revision,
                requestId = x.Command.RequestId,
                noteRef = x.Command.UnverifiedFollowUpNoteRef,
                claimedActorRef = x.Command.ClaimedFollowUpActorRef
            }).ToArray()
        };
    }

    private static IResult Denied(int status, string outcome) => Results.Json(new
    {
        contract = "TRN-TECH-075",
        syntheticOnly = true,
        memberAuthenticated = false,
        complaintLegallySubmitted = false,
        insurerContacted = false,
        clinicalReviewCompleted = false,
        nationalReleaseAllowed = false,
        outcome
    }, statusCode: status);

    private static async Task<SyntheticSupportCommand?> ParseAsync(HttpRequest request, string caseRef)
    {
        var bytes = await SyntheticCommandIngress.ReadAsync(request,1400);
        if (bytes is null) return null;
        try
        {
            using var json = JsonDocument.Parse(bytes.AsMemory(),
                new JsonDocumentOptions { MaxDepth = 4 });
            var v = json.RootElement;
            if (v.ValueKind != JsonValueKind.Object ||
                v.EnumerateObject().Count() != 10 ||
                v.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 10 ||
                !v.TryGetProperty("requestId", out var id) ||
                !v.TryGetProperty("expectedRevision", out var revision) ||
                !v.TryGetProperty("action", out var action) ||
                !v.TryGetProperty("memberRef", out var member) ||
                !v.TryGetProperty("topic", out var topic) ||
                !v.TryGetProperty("unverifiedEvidenceRef", out var evidence) ||
                !v.TryGetProperty("claimedSubmitterRef", out var submitter) ||
                !v.TryGetProperty("originalSubmissionRequestId", out var original) ||
                !v.TryGetProperty("claimedFollowUpActorRef", out var followActor) ||
                !v.TryGetProperty("unverifiedFollowUpNoteRef", out var note) ||
                id.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(id.GetString(), out var uuid) || uuid == Guid.Empty ||
                revision.ValueKind != JsonValueKind.Number ||
                !revision.TryGetInt64(out var version) || version < 0 || version == long.MaxValue ||
                action.ValueKind != JsonValueKind.String ||
                topic.ValueKind != JsonValueKind.String ||
                member.ValueKind != JsonValueKind.String ||
                evidence.ValueKind != JsonValueKind.String ||
                submitter.ValueKind != JsonValueKind.String ||
                original.ValueKind != JsonValueKind.String ||
                followActor.ValueKind is not (JsonValueKind.Null or JsonValueKind.String) ||
                note.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                return null;
            var a = action.GetString() switch
            {
                "SUBMIT_UNVERIFIED" => SyntheticSupportAction.SubmitUnverified,
                "FOLLOW_UP_UNVERIFIED" => SyntheticSupportAction.RecordUnverifiedFollowUp,
                _ => (SyntheticSupportAction)0
            };
            var t = topic.GetString() switch
            {
                "MEDICAL_SERVICE_COMPLAINT" => SyntheticSupportTopic.MedicalServiceComplaint,
                "INSURANCE_COVERAGE_DISPUTE" => SyntheticSupportTopic.InsuranceCoverageDispute,
                _ => (SyntheticSupportTopic)0
            };
            if (a == 0 || t == 0 ||
                !PgSyntheticSupportJournal.IsSynthetic(member.GetString()) ||
                !PgSyntheticSupportJournal.IsSynthetic(evidence.GetString()) ||
                !PgSyntheticSupportJournal.IsSynthetic(submitter.GetString()) ||
                !Guid.TryParse(original.GetString(), out var originalId))
                return null;
            var actor = followActor.ValueKind == JsonValueKind.String ? followActor.GetString() : null;
            var refNote = note.ValueKind == JsonValueKind.String ? note.GetString() : null;
            if (a == SyntheticSupportAction.SubmitUnverified)
            {
                if (originalId != Guid.Empty || actor is not null || refNote is not null)
                    return null;
            }
            else if (originalId == Guid.Empty ||
                !PgSyntheticSupportJournal.IsSynthetic(actor) ||
                !PgSyntheticSupportJournal.IsSynthetic(refNote))
                return null;

            return new SyntheticSupportCommand(
                uuid, version, a, caseRef, member.GetString()!, t, evidence.GetString()!,
                submitter.GetString()!, originalId, actor, refNote);
        }
        catch (JsonException) { return null; }
    }
}
