using System.Globalization;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-099 — reattested preflight for a manual synthetic support follow-up.
// The actual write is ONLY the preexisting revision-locked support command.
// This non-atomic cross-journal check is NOT an insurer decision or a lock.
internal static class CostSupportFollowUpHttp
{
    internal static async Task<IResult> Preflight(HttpContext ctx)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_COST_JOURNAL_DEMO") !=
            "CI_EPHEMERAL_ONLY") return Results.NotFound();
        var p = ctx.Request.RouteValues["previewId"]?.ToString();
        var c = ctx.Request.RouteValues["caseRef"]?.ToString();
        var q = ctx.Request.Query;
        if (!Guid.TryParseExact(p,"D",out var id) || id==Guid.Empty ||
            p!=id.ToString("D") || !PgSyntheticSupportJournal.IsSynthetic(c) ||
            q.Count!=2 || !q.ContainsKey("expectedCostChoiceRevision") ||
            !q.ContainsKey("expectedSupportRevision") ||
            q["expectedCostChoiceRevision"].Count!=1 ||
            q["expectedSupportRevision"].Count!=1 ||
            !Unsigned(q["expectedCostChoiceRevision"].ToString(),out var costVersion) ||
            !Unsigned(q["expectedSupportRevision"].ToString(),out var supportVersion) ||
            supportVersion==0)
            return Denied(400,"INVALID_TEST_FOLLOWUP_PREFLIGHT");

        try
        {
            var cost=await CostJournalHttp.Store.ReadAsync(id);
            if(cost is null) return Denied(404,"TEST_COST_PREVIEW_NOT_FOUND");
            var caseState=await SupportHttp.Store.ReadAsync(c!);
            if(caseState.History.IsEmpty) return Denied(404,"TEST_SUPPORT_CASE_NOT_FOUND");
            var original=caseState.History[0].Command;
            if(original.Action!=SyntheticSupportAction.SubmitUnverified ||
                original.Topic!=SyntheticSupportTopic.InsuranceCoverageDispute ||
                original.CaseRef!=c || original.MemberRef!=cost.MemberRef ||
                original.UnverifiedEvidenceRef!="test:cost-preview:"+p ||
                !PgSyntheticSupportJournal.IsSynthetic(original.ClaimedSubmitterRef))
                return Denied(409,"UNRELATED_TEST_SUPPORT_CASE");
            if(cost.Revision!=costVersion || caseState.Revision!=supportVersion)
                return Denied(409,"TEST_FOLLOWUP_SOURCE_CHANGED");

            // Recheck both source revisions and fingerprint, never promise
            // atomicity or exclusive locks across distinct PostgreSQL journals.
            var costNow=await CostJournalHttp.Store.ReadAsync(id);
            var supportNow=await SupportHttp.Store.ReadAsync(c!);
            if(costNow is null || costNow.Revision!=costVersion ||
                costNow.PreviewFingerprint!=cost.PreviewFingerprint ||
                costNow.PreviewRevision!=cost.PreviewRevision ||
                supportNow.Revision!=supportVersion)
                return Denied(409,"TEST_FOLLOWUP_SOURCE_CHANGED");
            return Results.Json(new
            {
                contract="TRN-TECH-099",syntheticOnly=true,
                outcome="UNVERIFIED_FOLLOWUP_PREFLIGHT",
                previewId=id,caseRef=c,memberRef=cost.MemberRef,
                previewRevision=cost.PreviewRevision,
                previewFingerprint=cost.PreviewFingerprint,
                costChoiceRevision=costVersion,supportRevision=supportVersion,
                originalSubmissionRequestId=original.RequestId,
                claimedSubmitterRef=original.ClaimedSubmitterRef,
                evidenceRef=original.UnverifiedEvidenceRef,
                independentlyReattested=true,atomicCrossJournalSnapshot=false,
                memberAuthenticated=false,insurerContacted=false,
                coverageAdjudicated=false,complaintLegallySubmitted=false,
                officialChoiceRecorded=false,walletMutated=false,
                financialCommitmentCreated=false,clinicalConsentGranted=false,
                nationalReleaseAllowed=false
            });
        }
        catch(Exception ex) when(ex is NpgsqlException or InvalidOperationException
                                   or IOException or OverflowException)
        {
            return Denied(503,"TEST_FOLLOWUP_SOURCES_UNVERIFIABLE");
        }
    }

    private static bool Unsigned(string? s,out long value)
    {
        value=0;
        return s is {Length:>0 and <=19} &&
            (s=="0"||s[0]!='0') && s.All(char.IsAsciiDigit) &&
            long.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out value) &&
            value>=0 && value<long.MaxValue;
    }

    private static IResult Denied(int status,string outcome)=>Results.Json(new
    {
        contract="TRN-TECH-099",syntheticOnly=true,outcome,
        independentlyReattested=false,atomicCrossJournalSnapshot=false,
        memberAuthenticated=false,insurerContacted=false,coverageAdjudicated=false,
        complaintLegallySubmitted=false,officialChoiceRecorded=false,
        walletMutated=false,financialCommitmentCreated=false,
        clinicalConsentGranted=false,nationalReleaseAllowed=false
    },statusCode:status);
}
