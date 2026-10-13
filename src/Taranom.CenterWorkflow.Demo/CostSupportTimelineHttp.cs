using System.Globalization;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-100 — synthetic, read-only cross-journal support timeline. Both
// sources are reattested by their existing PostgreSQL canonical journals.
// Neither the two reads nor the recheck form a distributed transaction.
internal static class CostSupportTimelineHttp
{
    internal static async Task<IResult> ReadPageAsync(HttpContext context)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_COST_JOURNAL_DEMO") !=
            "CI_EPHEMERAL_ONLY") return Results.NotFound();
        var rawPreview = context.Request.RouteValues["previewId"]?.ToString();
        var caseRef = context.Request.RouteValues["caseRef"]?.ToString();
        var query = context.Request.Query;
        if (!Guid.TryParseExact(rawPreview, "D", out var id) || id == Guid.Empty ||
            rawPreview != id.ToString("D") ||
            !PgSyntheticSupportJournal.IsSynthetic(caseRef) ||
            query.Count != 4 ||
            !query.ContainsKey("afterRevision") ||
            !query.ContainsKey("limit") ||
            !query.ContainsKey("expectedCostChoiceRevision") ||
            !query.ContainsKey("expectedSupportRevision") ||
            query["afterRevision"].Count != 1 ||
            query["limit"].Count != 1 ||
            query["expectedCostChoiceRevision"].Count != 1 ||
            query["expectedSupportRevision"].Count != 1 ||
            !Unsigned(query["afterRevision"].ToString(), out var after) ||
            !Unsigned(query["limit"].ToString(), out var limit) ||
            !Unsigned(query["expectedCostChoiceRevision"].ToString(), out var costRevision) ||
            !Unsigned(query["expectedSupportRevision"].ToString(), out var supportRevision) ||
            limit is < 1 or > 20 || supportRevision == 0)
            return Denied(400, "INVALID_TEST_CROSS_HISTORY_CURSOR");

        try
        {
            var cost = await CostJournalHttp.Store.ReadAsync(id);
            if (cost is null) return Denied(404, "TEST_COST_PREVIEW_NOT_FOUND");
            var support = await SupportHttp.Store.ReadAsync(caseRef!);
            if (support.History.IsEmpty)
                return Denied(404, "TEST_SUPPORT_CASE_NOT_FOUND");
            var first = support.History[0].Command;
            if (first.Action != SyntheticSupportAction.SubmitUnverified ||
                first.Topic != SyntheticSupportTopic.InsuranceCoverageDispute ||
                first.CaseRef != caseRef ||
                first.MemberRef != cost.MemberRef ||
                first.UnverifiedEvidenceRef != "test:cost-preview:" + rawPreview ||
                !PgSyntheticSupportJournal.IsSynthetic(first.ClaimedSubmitterRef))
                return Denied(409, "UNRELATED_TEST_SUPPORT_CASE");
            if (cost.Revision != costRevision ||
                support.Revision != supportRevision)
                return Denied(409, "CROSS_HISTORY_SOURCE_CHANGED");
            if (after > supportRevision)
                return Denied(400, "INVALID_TEST_CROSS_HISTORY_CURSOR");
            var page = support.History.Where(x => x.Revision > after)
                .Take((int)limit + 1).ToArray();
            var hasMore = page.Length > limit;
            var events = page.Take((int)limit).Select(x => new
            {
                revision = x.Revision,
                action = x.Command.Action == SyntheticSupportAction.SubmitUnverified
                    ? "SUBMIT_UNVERIFIED" : "FOLLOW_UP_UNVERIFIED",
                noteRef = x.Command.UnverifiedFollowUpNoteRef,
                hypotheticalOnly = true,
                insurerVerified = false
            }).ToArray();

            // Independently recheck the already-verified sources to catch
            // observable changes before returning a page. Not atomic.
            var costNow = await CostJournalHttp.Store.ReadAsync(id);
            var supportNow = await SupportHttp.Store.ReadAsync(caseRef!);
            if (costNow is null ||
                costNow.Revision != costRevision ||
                costNow.PreviewRevision != cost.PreviewRevision ||
                costNow.PreviewFingerprint != cost.PreviewFingerprint ||
                supportNow.Revision != supportRevision)
                return Denied(409, "CROSS_HISTORY_SOURCE_CHANGED");

            return Results.Json(new
            {
                contract = "TRN-TECH-100", syntheticOnly = true,
                outcome = "VERIFIED_UNVERIFIED_INSURANCE_TIMELINE",
                previewId = id, caseRef,
                previewRevision = cost.PreviewRevision,
                previewFingerprint = cost.PreviewFingerprint,
                costChoiceRevision = costRevision,
                supportRevision,
                originalSubmissionRequestId = first.RequestId,
                expectedCostChoiceRevision = costRevision,
                expectedSupportRevision = supportRevision,
                afterRevision = after,
                hasMore,
                nextAfterRevision = hasMore ? events[^1].revision : (long?)null,
                events,
                independentlyReattested = true,
                atomicCrossJournalSnapshot = false,
                memberAuthenticated = false, insurerContacted = false,
                coverageAdjudicated = false, complaintLegallySubmitted = false,
                officialChoiceRecorded = false, walletMutated = false,
                financialCommitmentCreated = false, clinicalConsentGranted = false,
                nationalReleaseAllowed = false
            });
        }
        catch (Exception error) when (error is NpgsqlException or
                                      InvalidOperationException or IOException or
                                      OverflowException)
        {
            return Denied(503, "TEST_CROSS_HISTORY_UNVERIFIABLE");
        }
    }

    private static bool Unsigned(string? value, out long parsed)
    {
        parsed = 0;
        return value is { Length: > 0 and <= 19 } &&
            (value == "0" || value[0] != '0') &&
            value.All(char.IsAsciiDigit) &&
            long.TryParse(value, NumberStyles.None,
                CultureInfo.InvariantCulture, out parsed) &&
            parsed >= 0 && parsed < long.MaxValue;
    }

    private static IResult Denied(int status, string outcome) =>
        Results.Json(new
        {
            contract = "TRN-TECH-100", syntheticOnly = true, outcome,
            independentlyReattested = false,
            atomicCrossJournalSnapshot = false,
            memberAuthenticated = false, insurerContacted = false,
            coverageAdjudicated = false, complaintLegallySubmitted = false,
            officialChoiceRecorded = false, walletMutated = false,
            financialCommitmentCreated = false, clinicalConsentGranted = false,
            nationalReleaseAllowed = false
        }, statusCode: status);
}
