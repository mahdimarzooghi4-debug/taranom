using System.Globalization;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-195: independent, source-verified, revision-pinned, test-only
// nonclinical coordination event history. NEVER a medical/consent record.
internal static class CareHistoryHttp
{
    private static readonly string[] Keys =
        ["afterRevision","limit","expectedCurrentRevision"];

    private static bool TestRef(string? value) =>
        value is {Length:>5 and <=100} &&
        value.StartsWith("test:",StringComparison.Ordinal) &&
        value.All(x=>char.IsAsciiLetterOrDigit(x)||x is ':' or '-' or '_');

    private static bool Number(string? text,out long value)
    {
        value=0;
        return text is {Length:>0 and <=19} &&
            (text=="0"||text[0]!='0') &&
            text.All(char.IsAsciiDigit) &&
            long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value) &&
            value>=0;
    }

    internal static async Task<IResult> ListAsync(HttpContext context)
    {
        var caseRef=context.Request.RouteValues["caseRef"]?.ToString();
        var query=context.Request.Query;
        if(!TestRef(caseRef)||query.Count is <3 or >4||
           Keys.Any(key=>!query.ContainsKey(key)||query[key].Count!=1)||
           (query.Count==4&&(!query.ContainsKey("claimType")||
                             query["claimType"].Count!=1))||
           !Number(query["afterRevision"].ToString(),out var after)||
           !Number(query["limit"].ToString(),out var limit)||limit is <1 or >10||
           !Number(query["expectedCurrentRevision"].ToString(),out var pin)||
           after>pin)return Denied(400,"INVALID_TEST_CARE_HISTORY_CURSOR");
        // A claim filter observes only immutable, unverified event types.
        // ALL is the exact backwards-compatible legacy path.
        var claimType=query.ContainsKey("claimType")
            ?query["claimType"].ToString():"ALL";
        if(claimType is not ("ALL" or "CONTACT" or "SOURCE"))
            return Denied(400,"INVALID_TEST_CARE_HISTORY_CLAIM_TYPE");
        try
        {
            // The store replays ALL events and paired digests from a single
            // PostgreSQL REPEATABLE READ READ ONLY reader transaction.
            var journal=await CareCoordinationHttp.Store.ReadAsync(caseRef!);
            if(pin!=journal.Revision)
                return Denied(409,"TEST_CARE_HISTORY_REVISION_CHANGED");
            // Reattest the entire journal before selecting any claim type.
            // The cursor is always the original persisted revision rather
            // than an ordinal index in sparse filtered history.
            var raw=journal.History.Where(e=>e.Revision>after &&
                (claimType=="ALL"||
                 (claimType=="CONTACT"&&e.Command.Action.ToString()=="RecordUnverifiedCoordinatorContact")||
                 (claimType=="SOURCE"&&e.Command.Action.ToString()=="AttachUnverifiedClinicalSourceReference")))
                .Take((int)limit+1).ToArray();
            var hasMore=raw.Length>limit;
            var entries=raw.Take((int)limit).Select(e=>new
            {
                revision=e.Revision,
                action=e.Command.Action.ToString(),
                requestId=e.Command.RequestId,
                memberRef=e.Command.MemberRef,
                originalMemberRequestRef=e.Command.OriginalMemberRequestRef,
                originalSubmissionRequestId=e.Command.OriginalSubmissionRequestId,
                claimedActorRef=e.Command.ClaimedActorRef,
                unverifiedNoteOrDocumentRef=e.Command.UnverifiedNoteOrDocumentRef
            }).ToArray();
            return Results.Json(new
            {
                contract="TRN-TECH-195",
                syntheticOnly=true,unverifiedClaimsOnly=true,
                source="VERIFIED_ISOLATED_POSTGRES_CARE_JOURNAL",
                caseRef,currentRevision=journal.Revision,expectedCurrentRevision=pin,
                afterRevision=after,limit,claimType,hasMore,
                nextAfterRevision=hasMore?entries[^1].revision:(long?)null,
                events=entries,
                realMemberAuthenticated=false,realCoordinatorVerified=false,
                clinicalSourceVerified=false,clinicianAuthorized=false,
                referralOrdered=false,referralDelivered=false,
                appointmentBooked=false,treatmentStarted=false,
                medicalRecordAccessGranted=false,emergencyServiceDispatched=false,
                consentGranted=false,walletMutated=false,nationalReleaseAllowed=false
            });
        }
        catch(Exception ex) when(ex is NpgsqlException or InvalidOperationException
                             or IOException or OverflowException)
        {
            return Denied(503,"TEST_CARE_HISTORY_UNVERIFIABLE");
        }
    }

    private static IResult Denied(int status,string outcome)=>Results.Json(new
    {
        contract="TRN-TECH-195",outcome,syntheticOnly=true,
        unverifiedClaimsOnly=true,
        realMemberAuthenticated=false,realCoordinatorVerified=false,
        clinicalSourceVerified=false,clinicianAuthorized=false,
        referralOrdered=false,referralDelivered=false,appointmentBooked=false,
        treatmentStarted=false,medicalRecordAccessGranted=false,
        emergencyServiceDispatched=false,consentGranted=false,
        walletMutated=false,nationalReleaseAllowed=false
    },statusCode:status);
}
