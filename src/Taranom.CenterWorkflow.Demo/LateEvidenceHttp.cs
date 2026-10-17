using System.Globalization;
using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;
using Taranom.ProofingJournal.Postgres;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-114: exercise the ORIGINAL pure LateEvidenceWorkItem + real
// transactionally audited PG source only on CI ephemeral localhost.
// Claimed reviewers are not authenticated. No entitlement or money posting.
internal static class LateEvidenceHttp
{
    internal static PgLateEvidenceWorkItemJournal Store { get; set; } = null!;

    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal)
    {
        "requestId", "expectedRevision", "action", "memberRef",
        "periodStartInclusive", "periodEndExclusive", "evidenceRef",
        "submittedByClaimRef", "originalSubmissionRequestId",
        "reviewerClaimRef", "reviewNoteEvidenceRef"
    };
    private static bool TestRef(string? value) =>
        value is { Length: > 5 and <= 100 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is ':' or '_' or '-');

    internal static async Task<IResult> ReadAsync(HttpContext ctx)
    {
        var id = ctx.Request.RouteValues["caseRef"]?.ToString();
        if (!TestRef(id) || ctx.Request.Query.Count > 0)
            return Reply(400,"INVALID_TEST_INPUT",id,null);
        try
        {
            return Reply(200,"READ",id,await Store.ReadAsync(id!));
        }
        catch (Exception ex) when (ex is NpgsqlException or IOException or
                                      InvalidOperationException or OverflowException)
        {
            return Reply(503,"SOURCE_INTEGRITY_UNVERIFIABLE",id,null);
        }
    }

    internal static async Task<IResult> WriteAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var id = req.RouteValues["caseRef"]?.ToString();
        if (!TestRef(id))return Reply(400,"INVALID_TEST_INPUT",id,null);
        if (req.Headers.Origin.ToString() is not
            ("http://127.0.0.1:4320" or "http://localhost:4320"))
            return Reply(403,"ORIGIN_DENIED",id,null);
        if (req.Query.Count > 0 || req.ContentType is null ||
            !req.ContentType.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)
            || req.ContentLength is > 1400)
            return Reply(400,"INVALID_TEST_INPUT",id,null);

        try
        {
            var body=await SyntheticCommandIngress.ReadAsync(req,1400);
            if(body is null)return Reply(400,"INVALID_TEST_INPUT",id,null);
            using var document=JsonDocument.Parse(body.AsMemory(),
                new JsonDocumentOptions{MaxDepth=3});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object ||
               root.EnumerateObject().Count()!=Keys.Count ||
               root.EnumerateObject().Select(x=>x.Name).ToHashSet(
                   StringComparer.Ordinal).Count!=Keys.Count ||
               root.EnumerateObject().Any(x=>!Keys.Contains(x.Name)))
                return Reply(400,"INVALID_TEST_INPUT",id,null);

            string? Str(string key)
            {
                var x=root.GetProperty(key);
                return x.ValueKind==JsonValueKind.String ? x.GetString() : null;
            }
            string? Optional(string key)
            {
                var x=root.GetProperty(key);
                return x.ValueKind==JsonValueKind.Null ? null :
                    x.ValueKind==JsonValueKind.String ? x.GetString() : "";
            }
            var revisionValue=root.GetProperty("expectedRevision");
            if(revisionValue.ValueKind!=JsonValueKind.Number ||
               !revisionValue.TryGetInt64(out var rev) ||
               rev is < 0 or >= long.MaxValue)
                return Reply(400,"INVALID_TEST_INPUT",id,null);
            var request=Str("requestId");
            var original=Str("originalSubmissionRequestId");
            var member=Str("memberRef");
            var submitter=Str("submittedByClaimRef");
            var evidence=Str("evidenceRef");
            var reviewer=Optional("reviewerClaimRef");
            var note=Optional("reviewNoteEvidenceRef");
            var action=Str("action") switch
            {
                "SUBMIT_UNVERIFIED_EVIDENCE"=>LateEvidenceAction.SubmitUnverifiedEvidence,
                "RECORD_UNVERIFIED_HUMAN_NOTE"=>LateEvidenceAction.RecordUnverifiedHumanNote,
                _=>(LateEvidenceAction)0
            };
            if(action==0 || !TestRef(member) || !TestRef(evidence) ||
               !TestRef(submitter) || (reviewer is not null && !TestRef(reviewer)) ||
               (note is not null && !TestRef(note)) ||
               !Guid.TryParseExact(request,"D",out var requestId) ||
               requestId==Guid.Empty || requestId.ToString("D")[14]!='4' ||
               !Guid.TryParseExact(original,"D",out var originalId) ||
               !DateTimeOffset.TryParseExact(Str("periodStartInclusive"),
                   "yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,
                   out var start) ||
               !DateTimeOffset.TryParseExact(Str("periodEndExclusive"),
                   "yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,
                   out var end) || start>=end)
                return Reply(400,"INVALID_TEST_INPUT",id,null);

            if(action==LateEvidenceAction.SubmitUnverifiedEvidence
                ? originalId!=Guid.Empty || reviewer is not null || note is not null
                : originalId==Guid.Empty || originalId.ToString("D")[14]!='4' ||
                  reviewer is null || note is null)
                return Reply(400,"INVALID_TEST_INPUT",id,null);
            var command=new LateEvidenceCommand(requestId,rev,action,id!,member!,
                start,end,evidence!,submitter!,originalId,reviewer,note);
            var result=await Store.ApplyAsync(command);
            var status=result.Outcome switch
            {
                LateEvidenceOutcome.RecordedUnverified or LateEvidenceOutcome.Replayed=>200,
                LateEvidenceOutcome.Invalid=>400,
                _=>409
            };
            return Reply(status,result.Outcome.ToString(),id,result.Snapshot);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return Reply(400,"INVALID_TEST_INPUT",id,null);
        }
        catch (OperationCanceledException)
        {return Reply(503,"SOURCE_INTEGRITY_UNVERIFIABLE",id,null);}
        catch (Exception ex) when (ex is NpgsqlException or
                                      InvalidOperationException or OverflowException)
        {
            return Reply(503,"SOURCE_INTEGRITY_UNVERIFIABLE",id,null);
        }
    }
    private static IResult Reply(int status,string outcome,string? caseRef,
        LateEvidenceCaseSnapshot? snap)=>Results.Json(new
    {
        contract="TRN-TECH-114",syntheticOnly=true,untrustedClaimsOnly=true,
        caseRef,outcome,revision=snap?.Revision,
        events=snap?.Events.Select(e=>new
        {
            revision=e.Revision, action=e.Command.Action.ToString(),
            requestId=e.Command.RequestId,memberRef=e.Command.MemberRef,
            evidenceRef=e.Command.EvidenceRef,
            submittedByClaimRef=e.Command.SubmittedByClaimRef,
            originalSubmissionRequestId=e.Command.OriginalSubmissionRequestId,
            reviewerClaimRef=e.Command.ReviewerClaimRef,
            reviewNoteEvidenceRef=e.Command.ReviewNoteEvidenceRef,
            periodStartInclusive=e.Command.PeriodStartInclusive.ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture),
            periodEndExclusive=e.Command.PeriodEndExclusive.ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture)
        }).ToArray()??Array.Empty<object>(),
        realMemberAuthenticated=false,realReviewerAuthenticated=false,
        officialMembershipVerified=false,legalEvidenceVerified=false,
        evidenceSourceAuthenticated=false,entitlementApproved=false,
        benefitAmountCalculated=false,walletCredited=false,
        financialLedgerMutated=false,clinicalAccessGranted=false,
        nationalReleaseAllowed=false
    },statusCode:status);
}
