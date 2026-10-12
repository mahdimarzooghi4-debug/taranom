using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-097: correlate two EXISTING append-only synthetic PostgreSQL
// journals. This is a reference-only cross-check, not an adjudication,
// insurer integration, new ledger or endorsement of claimed evidence.
internal static class CostSupportHandoffHttp
{
    internal static async Task<IResult> Inspect(HttpContext context)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_COST_JOURNAL_DEMO") !=
            "CI_EPHEMERAL_ONLY") return Results.NotFound();
        var req = context.Request;
        var rawPreview = req.RouteValues["previewId"]?.ToString();
        var caseRef = req.RouteValues["caseRef"]?.ToString();
        if (req.Query.Count != 0 ||
            !Guid.TryParseExact(rawPreview, "D", out var id) || id == Guid.Empty ||
            rawPreview != id.ToString("D") ||
            !PgSyntheticSupportJournal.IsSynthetic(caseRef))
            return Denied(400, "INVALID_TEST_LINK_REF");

        try
        {
            var cost = await CostJournalHttp.Store.ReadAsync(id);
            if (cost is null) return Denied(404, "TEST_COST_PREVIEW_NOT_FOUND");
            var support = await SupportHttp.Store.ReadAsync(caseRef!);
            if (support.Revision == 0 || support.History.IsEmpty)
                return Denied(404, "TEST_SUPPORT_CASE_NOT_FOUND");
            var original = support.History[0].Command;
            var expectedEvidence = "test:cost-preview:" + id.ToString("D");
            if (original.Topic != SyntheticSupportTopic.InsuranceCoverageDispute ||
                original.Action != SyntheticSupportAction.SubmitUnverified ||
                original.CaseRef != caseRef ||
                original.MemberRef != cost.MemberRef ||
                original.UnverifiedEvidenceRef != expectedEvidence)
                return Denied(409, "UNRELATED_TEST_SUPPORT_CASE");

            // These independent journals have no distributed transaction.
            // Reattest each exact current revision to avoid returning an
            // obviously stale mixed snapshot. This is informational ONLY.
            var lastCost = await CostJournalHttp.Store.ReadAsync(id);
            var lastSupport = await SupportHttp.Store.ReadAsync(caseRef!);
            if (lastCost is null || lastSupport.Revision != support.Revision ||
                lastCost.Revision != cost.Revision ||
                lastCost.PreviewRevision != cost.PreviewRevision ||
                lastCost.PreviewFingerprint != cost.PreviewFingerprint)
                return Denied(409, "STALE_TEST_SOURCE_REVIEW");
            return Results.Json(new
            {
                contract = "TRN-TECH-097", syntheticOnly = true,
                outcome = "UNVERIFIED_CROSS_JOURNAL_REFERENCE",
                previewId = id, caseRef,
                memberRef = cost.MemberRef,
                previewRevision = cost.PreviewRevision,
                previewFingerprint = cost.PreviewFingerprint,
                costChoiceRevision = cost.Revision,
                supportRevision = support.Revision,
                supportFollowUpCount = support.History.Length - 1,
                topic = "InsuranceCoverageDispute",
                evidenceRef = expectedEvidence,
                independentlyReattested = true,
                atomicCrossJournalSnapshot = false,
                memberAuthenticated = false,
                insurerContacted = false,
                coverageAdjudicated = false,
                complaintLegallySubmitted = false,
                officialChoiceRecorded = false,
                walletMutated = false,
                financialCommitmentCreated = false,
                clinicalConsentGranted = false,
                nationalReleaseAllowed = false
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException
                                  or IOException or OverflowException)
        {
            return Denied(503, "TEST_JOURNALS_UNVERIFIABLE");
        }
    }

    private static IResult Denied(int code, string outcome) => Results.Json(new
    {
        contract = "TRN-TECH-097", syntheticOnly = true, outcome,
        independentlyReattested = false, atomicCrossJournalSnapshot = false,
        memberAuthenticated = false, insurerContacted = false,
        coverageAdjudicated = false, complaintLegallySubmitted = false,
        officialChoiceRecorded = false, walletMutated = false,
        financialCommitmentCreated = false, clinicalConsentGranted = false,
        nationalReleaseAllowed = false
    }, statusCode: code);
}
