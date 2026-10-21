using System.Globalization;
using Npgsql;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-186 / CI-ephemeral only: NOT a real member directory,
// clinical triage list, coordinator authorization, patient consent or access.
internal static class CareWorklistHttp
{
    internal static async Task<IResult> ListAsync(HttpContext ctx)
    {
        var q=ctx.Request.Query;
        if(q.Count is <2 or >3||!q.ContainsKey("afterCaseRef")||!q.ContainsKey("limit")||
           q["afterCaseRef"].Count!=1||q["limit"].Count!=1||
           (q.Count==3&&(!q.ContainsKey("claimFilter")||q["claimFilter"].Count!=1)))
            return Deny(400,"INVALID_TEST_CARE_WORKLIST_CURSOR");
        var after=q["afterCaseRef"].ToString();
        var count=q["limit"].ToString();
        // Old callers remain ALL; no clinical/task-state semantics are inferred.
        var filter=q.ContainsKey("claimFilter")?q["claimFilter"].ToString():"ALL";
        if(filter is not ("ALL" or "HAS_CONTACT" or "HAS_SOURCE" or "HAS_BOTH")||
           (after.Length>0 && !TestRef(after))||
           count.Length is <1 or >2||count[0]=='0'||
           !count.All(char.IsAsciiDigit)||
           !int.TryParse(count,NumberStyles.None,CultureInfo.InvariantCulture,out var limit)||
           limit is <1 or >10)
            return Deny(400,"INVALID_TEST_CARE_WORKLIST_CURSOR");
        try
        {
            var (rows,more)=await CareCoordinationHttp.Store.ReadWorklistAsync(
                after.Length==0?null:after,limit,filter);
            var cases=rows.Select(x=>new
            {
                caseRef=x.CaseRef,revision=x.Revision,
                contactClaims=x.ContactClaims,
                sourceClaims=x.SourceClaims
            }).ToArray();
            return Results.Json(new
            {
                contract="TRN-TECH-186",syntheticOnly=true,
                unverifiedClaimsOnly=true,source="VERIFIED_ISOLATED_POSTGRES_CARE_JOURNAL",
                realMemberAuthenticated=false,realCoordinatorVerified=false,
                clinicianAuthorized=false,clinicalSourceVerified=false,
                referralOrdered=false,referralDelivered=false,
                appointmentBooked=false,treatmentStarted=false,
                medicalRecordAccessGranted=false,emergencyServiceDispatched=false,
                consentGranted=false,walletMutated=false,nationalReleaseAllowed=false,
                afterCaseRef=after,limit,claimFilter=filter,hasMore=more,
                nextAfterCaseRef=more?cases[^1].caseRef:null,
                cases
            });
        }
        catch(Exception e) when(e is NpgsqlException or IOException or
            InvalidOperationException or OverflowException)
        {return Deny(503,"TEST_CARE_WORKLIST_UNVERIFIABLE");}
    }
    private static bool TestRef(string value)=>value.Length is >5 and <=128&&
        value.StartsWith("test:",StringComparison.Ordinal)&&
        value.AsSpan(5).ToArray().All(c=>char.IsAsciiLetterOrDigit(c)||
            c is '-' or '_' or ':');
    private static IResult Deny(int status,string outcome)=>Results.Json(new
    {
        contract="TRN-TECH-186",outcome,syntheticOnly=true,
        unverifiedClaimsOnly=true,realMemberAuthenticated=false,
        realCoordinatorVerified=false,clinicianAuthorized=false,
        clinicalSourceVerified=false,referralOrdered=false,
        referralDelivered=false,appointmentBooked=false,treatmentStarted=false,
        medicalRecordAccessGranted=false,emergencyServiceDispatched=false,
        consentGranted=false,walletMutated=false,nationalReleaseAllowed=false,
        cases=Array.Empty<object>()
    },statusCode:status);
}
