using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-115: claim-only coordination. Not patient communications, live
// referrals, clinician approval or consent to disclose medical records.
internal static class CareCoordinationHttp
{
    internal static PgSyntheticCareCoordinationJournal Store {get;set;} = null!;
    private static readonly HashSet<string> Keys=new(StringComparer.Ordinal)
    {
        "requestId","expectedRevision","action","memberRef",
        "originalMemberRequestRef","originalSubmissionRequestId",
        "claimedActorRef","unverifiedNoteOrDocumentRef"
    };
    private static bool TestRef(string? v)=>v is {Length:>5 and <=100}&&
        v.StartsWith("test:",StringComparison.Ordinal)&&
        v.All(c=>char.IsAsciiLetterOrDigit(c)||c is ':' or '-' or '_');
    internal static async Task<IResult> ReadAsync(HttpContext ctx)
    {
        var c=ctx.Request.RouteValues["caseRef"]?.ToString();
        if(!TestRef(c)||ctx.Request.Query.Count>0)return Reply(400,"INVALID_TEST_INPUT",c,null);
        try{return Reply(200,"READ",c,await Store.ReadAsync(c!));}
        catch(Exception ex)when(ex is NpgsqlException or InvalidOperationException
                               or IOException or OverflowException)
        {return Reply(503,"SOURCE_UNVERIFIABLE",c,null);}
    }
    internal static async Task<IResult> WriteAsync(HttpContext ctx)
    {
        var req=ctx.Request;
        var c=req.RouteValues["caseRef"]?.ToString();
        if(!TestRef(c))return Reply(400,"INVALID_TEST_INPUT",c,null);
        if(req.Headers.Origin.ToString() is not
           ("http://127.0.0.1:4320" or "http://localhost:4320"))
            return Reply(403,"ORIGIN_DENIED",c,null);
        if(req.Query.Count>0||req.ContentType is null||
           !req.ContentType.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)||
           req.ContentLength is >1200)return Reply(400,"INVALID_TEST_INPUT",c,null);
        var buffer=new byte[1201];
        var len=0;
        try
        {
            while(len<buffer.Length)
            {
                var n=await req.Body.ReadAsync(buffer.AsMemory(len));
                if(n==0)break;
                len+=n;
            }
            if(len is <2 or >1200)return Reply(400,"INVALID_TEST_INPUT",c,null);
            using var doc=JsonDocument.Parse(buffer.AsMemory(0,len),
                new JsonDocumentOptions{MaxDepth=3});
            var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||
               root.EnumerateObject().Count()!=Keys.Count||
               root.EnumerateObject().Select(x=>x.Name).ToHashSet(
                   StringComparer.Ordinal).Count!=Keys.Count||
               root.EnumerateObject().Any(x=>!Keys.Contains(x.Name)))
                return Reply(400,"INVALID_TEST_INPUT",c,null);
            string? Text(string name)
            {
                var x=root.GetProperty(name);
                return x.ValueKind==JsonValueKind.String ? x.GetString():null;
            }
            string? Optional(string name)
            {
                var x=root.GetProperty(name);
                return x.ValueKind==JsonValueKind.Null?null:
                    x.ValueKind==JsonValueKind.String?x.GetString():"";
            }
            var version=root.GetProperty("expectedRevision");
            if(version.ValueKind!=JsonValueKind.Number||
               !version.TryGetInt64(out var rev)||rev is <0 or >=long.MaxValue)
                return Reply(400,"INVALID_TEST_INPUT",c,null);
            var reqId=Text("requestId");
            var orig=Text("originalSubmissionRequestId");
            var member=Text("memberRef");
            var context=Text("originalMemberRequestRef");
            var actor=Optional("claimedActorRef");
            var evidence=Optional("unverifiedNoteOrDocumentRef");
            var action=Text("action") switch
            {
                "REQUEST_NONCLINICAL_COORDINATION"=>
                    SyntheticCareTrailAction.RequestNonclinicalCoordination,
                "RECORD_UNVERIFIED_COORDINATOR_CONTACT"=>
                    SyntheticCareTrailAction.RecordUnverifiedCoordinatorContact,
                "ATTACH_UNVERIFIED_CLINICAL_SOURCE"=>
                    SyntheticCareTrailAction.AttachUnverifiedClinicalSourceReference,
                _=>(SyntheticCareTrailAction)0
            };
            if(action==0||!TestRef(member)||!TestRef(context)||
               (actor is not null && !TestRef(actor))||
               (evidence is not null && !TestRef(evidence))||
               !Guid.TryParseExact(reqId,"D",out var id)||id==Guid.Empty||
               id.ToString("D")[14]!='4'||
               !Guid.TryParseExact(orig,"D",out var origin))
                return Reply(400,"INVALID_TEST_INPUT",c,null);
            if(action==SyntheticCareTrailAction.RequestNonclinicalCoordination?
               origin!=Guid.Empty||actor is not null||evidence is not null:
               origin==Guid.Empty||origin.ToString("D")[14]!='4'||
               actor is null||evidence is null)
                return Reply(400,"INVALID_TEST_INPUT",c,null);
            var input=new SyntheticCareTrailCommand(id,rev,action,c!,member!,
                context!,origin,actor,evidence);
            var outcome=await Store.ApplyAsync(input);
            var code=outcome.Outcome switch
            {
                SyntheticCareTrailOutcome.RecordedUnverified or
                SyntheticCareTrailOutcome.Replayed=>200,
                SyntheticCareTrailOutcome.Invalid=>400,
                _=>409
            };
            return Reply(code,outcome.Outcome.ToString(),c,outcome.Snapshot);
        }
        catch(Exception ex)when(ex is JsonException or IOException)
        {return Reply(400,"INVALID_TEST_INPUT",c,null);}
        catch(Exception ex)when(ex is NpgsqlException or InvalidOperationException
                               or OverflowException)
        {return Reply(503,"SOURCE_UNVERIFIABLE",c,null);}
    }
    private static IResult Reply(int status,string outcome,string? caseRef,
        SyntheticCareTrailSnapshot? snap)=>Results.Json(new
    {
        contract="TRN-TECH-115",syntheticOnly=true,unverifiedClaimsOnly=true,
        caseRef,outcome,revision=snap?.Revision,
        events=snap?.History.Select(e=>new{
            revision=e.Revision,action=e.Command.Action.ToString(),
            requestId=e.Command.RequestId,memberRef=e.Command.MemberRef,
            originalMemberRequestRef=e.Command.OriginalMemberRequestRef,
            originalSubmissionRequestId=e.Command.OriginalSubmissionRequestId,
            claimedActorRef=e.Command.ClaimedActorRef,
            unverifiedNoteOrDocumentRef=e.Command.UnverifiedNoteOrDocumentRef
        }).ToArray()??Array.Empty<object>(),
        realMemberAuthenticated=false,realCoordinatorVerified=false,
        clinicalSourceVerified=false,clinicianAuthorized=false,
        referralOrdered=false,referralDelivered=false,appointmentBooked=false,
        treatmentStarted=false,medicalRecordAccessGranted=false,
        emergencyServiceDispatched=false,consentGranted=false,
        walletMutated=false,nationalReleaseAllowed=false
    },statusCode:status);
}
