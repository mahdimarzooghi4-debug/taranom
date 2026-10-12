using System.Text.Json;
using Npgsql;
using Taranom.MemberGovernance;
using Taranom.ProofingJournal.Postgres;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-095: isolated same-origin cost journal backed by the EXISTING
// PgSyntheticCostAwarenessJournal. No financial account or authenticated member.
internal static class CostJournalHttp
{
    internal static PgSyntheticCostAwarenessJournal Store { get; set; } = null!;
    private const string Contract = "TRN-TECH-095";

    internal static async Task<IResult> Register(HttpContext ctx)
    {
        if (!Enabled()) return Results.NotFound();
        if (!Origin(ctx.Request)) return Denied(403, "ORIGIN_DENIED");
        var doc = await ReadJson(ctx.Request, 8192);
        if (doc is null) return Denied(400,"INVALID_TEST_PREVIEW");
        using (doc)
        {
            if (!TryPreview(doc.RootElement, out var preview))
                return Denied(400,"INVALID_TEST_PREVIEW");
            try
            {
                var outcome = await Store.RegisterAsync(preview);
                if (outcome is SyntheticCostRegistration.Invalid)
                    return Denied(400,"INVALID_TEST_PREVIEW");
                if (outcome is SyntheticCostRegistration.ConflictingSnapshot)
                    return Denied(409,"CONFLICTING_IMMUTABLE_PREVIEW");
                var current = await Store.ReadAsync(preview.PreviewId);
                if (current is null) return Denied(503,"TEST_COST_JOURNAL_UNVERIFIABLE");
                return Results.Json(ReadDto(current,outcome.ToString()));
            }
            catch (Exception e) when (e is NpgsqlException or InvalidOperationException or IOException)
            {
                return Denied(503,"TEST_COST_JOURNAL_UNVERIFIABLE");
            }
        }
    }

    internal static async Task<IResult> Read(HttpContext ctx)
    {
        if (!Enabled()) return Results.NotFound();
        if (!TryRouteId(ctx, out var id)) return Denied(400,"INVALID_TEST_PREVIEW_ID");
        try
        {
            var state=await Store.ReadAsync(id);
            return state is null
                ? Denied(404,"TEST_PREVIEW_NOT_FOUND")
                : Results.Json(ReadDto(state,"READ"));
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or IOException)
        {
            return Denied(503,"TEST_COST_JOURNAL_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> Record(HttpContext ctx)
    {
        if (!Enabled()) return Results.NotFound();
        if (!Origin(ctx.Request)) return Denied(403,"ORIGIN_DENIED");
        if (!TryRouteId(ctx, out var id)) return Denied(400,"INVALID_TEST_PREVIEW_ID");
        var doc=await ReadJson(ctx.Request,1024);
        if (doc is null) return Denied(400,"INVALID_TEST_CHOICE_COMMAND");
        using (doc)
        {
            if (!TryCommand(doc.RootElement,id,out var command))
                return Denied(400,"INVALID_TEST_CHOICE_COMMAND");
            try
            {
                var change=await Store.RecordAsync(command);
                if (change.Outcome is CostAwarenessOutcome.Invalid)
                    return Denied(400,"INVALID_TEST_CHOICE_COMMAND");
                if (change.Snapshot is null)
                    return Denied(404,"TEST_PREVIEW_NOT_FOUND");
                var accepted = change.Outcome is CostAwarenessOutcome.RecordedUnverified or
                    CostAwarenessOutcome.Replayed;
                // Read-after-write fully reattests persisted event + audit even on replay.
                var current = await Store.ReadAsync(id);
                if (current is null) return Denied(503,"TEST_COST_JOURNAL_UNVERIFIABLE");
                return Results.Json(ReadDto(current, change.Outcome.ToString()),
                    statusCode: accepted ? 200 : 409);
            }
            catch (Exception e) when (e is NpgsqlException or InvalidOperationException or IOException)
            {
                return Denied(503,"TEST_COST_JOURNAL_UNVERIFIABLE");
            }
        }
    }

    private static object ReadDto(CostAwarenessSnapshot state,string outcome) => new
    {
        contract=Contract,syntheticOnly=true, memberAuthenticated=false,
        officialChoiceRecorded=false, choiceSignatureValid=false,
        financialCommitmentCreated=false,clinicalConsentGranted=false,
        walletMutated=false, insurerEntitlementVerified=false,
        officialEstimateVerified=false,nationalReleaseAllowed=false,
        outcome,previewId=state.PreviewId,memberRef=state.MemberRef,
        previewRevision=state.PreviewRevision,previewFingerprint=state.PreviewFingerprint,
        choiceRevision=state.Revision,historyCount=state.History.Length,
        lastUnverifiedChoice=state.History.IsEmpty ? null :
            state.History[^1].Command.Choice.ToString()
    };
    private static IResult Denied(int status,string outcome) => Results.Json(new
    {
        contract=Contract,syntheticOnly=true,memberAuthenticated=false,
        officialChoiceRecorded=false, choiceSignatureValid=false,
        financialCommitmentCreated=false, clinicalConsentGranted=false,
        walletMutated=false,insurerEntitlementVerified=false,
        officialEstimateVerified=false,nationalReleaseAllowed=false,outcome
    },statusCode:status);
    private static bool Enabled() =>
        Environment.GetEnvironmentVariable("TARANOM_COST_JOURNAL_DEMO")=="CI_EPHEMERAL_ONLY";
    private static bool Origin(HttpRequest req) =>
        req.Headers.Origin.ToString() is "http://127.0.0.1:4320" or "http://localhost:4320";
    private static bool TryRouteId(HttpContext ctx,out Guid id)
    {
        id=Guid.Empty;
        return ctx.Request.Query.Count==0 &&
            Guid.TryParseExact(ctx.Request.RouteValues["previewId"]?.ToString(),"D",out id) &&
            id!=Guid.Empty;
    }
    private static async Task<JsonDocument?> ReadJson(HttpRequest req,int max)
    {
        if (req.Query.Count!=0 || req.ContentType is null ||
            !req.ContentType.StartsWith("application/json",StringComparison.OrdinalIgnoreCase) ||
            req.ContentLength is > 0 && req.ContentLength>max)
            return null;
        var data=new byte[max+1];var length=0;
        try
        {
            while (length<data.Length)
            {
                var n=await req.Body.ReadAsync(data.AsMemory(length));
                if(n==0)break;
                length+=n;
            }
            if(length<2||length>max)return null;
            return JsonDocument.Parse(data.AsMemory(0,length),
                new JsonDocumentOptions {MaxDepth=5});
        }
        catch (Exception e) when(e is JsonException or IOException) {return null;}
    }
    private static bool Keys(JsonElement v,params string[] keys)
    {
        if(v.ValueKind!=JsonValueKind.Object)return false;
        var names=v.EnumerateObject().Select(p=>p.Name).ToArray();
        return names.Length==keys.Length &&
            names.Distinct(StringComparer.Ordinal).Count()==names.Length &&
            names.ToHashSet(StringComparer.Ordinal).SetEquals(keys);
    }
    private static bool Str(JsonElement v,out string? s)
    {
        s=null;
        if(v.ValueKind==JsonValueKind.Null)return true;
        if(v.ValueKind!=JsonValueKind.String)return false;
        s=v.GetString();return true;
    }
    private static bool Int(JsonElement v,out long n)
    {
        n=0;
        return v.ValueKind==JsonValueKind.Number && v.TryGetInt64(out n);
    }
    private static bool Uuid(JsonElement v,out Guid id)
    {
        id=Guid.Empty;
        return v.ValueKind==JsonValueKind.String &&
            Guid.TryParseExact(v.GetString(),"D",out id) && id!=Guid.Empty;
    }
    private static bool NullableUuid(JsonElement v,out Guid id)
    {
        id=Guid.Empty;
        return v.ValueKind==JsonValueKind.Null || Uuid(v,out id);
    }

    private static bool TryPreview(JsonElement v,out SyntheticCostPreview preview)
    {
        preview=default;
        if(!Keys(v,"memberRef","previewId","previewRevision","lines") ||
            !Str(v.GetProperty("memberRef"),out var member) ||
            !Uuid(v.GetProperty("previewId"),out var id) ||
            !Int(v.GetProperty("previewRevision"),out var revision) ||
            v.GetProperty("lines").ValueKind!=JsonValueKind.Array ||
            v.GetProperty("lines").GetArrayLength()!=5)return false;
        var lines=new List<SyntheticCostWorkbenchLine>(5);
        foreach(var x in v.GetProperty("lines").EnumerateArray())
        {
            if(!Keys(x,"component","state","amount","currency","sourceRef",
                    "evidenceRef","sourceRevision") ||
                !Str(x.GetProperty("component"),out var component) ||
                !Str(x.GetProperty("state"),out var state) ||
                !Str(x.GetProperty("amount"),out var amount) ||
                !Str(x.GetProperty("currency"),out var currency) ||
                !Str(x.GetProperty("sourceRef"),out var source) ||
                !NullableUuid(x.GetProperty("evidenceRef"),out var evidence) ||
                !Int(x.GetProperty("sourceRevision"),out var sourceRevision))
                return false;
            lines.Add(new(component,state,amount,currency,source,
                evidence==Guid.Empty?null:evidence,sourceRevision));
        }
        preview=SyntheticCostWorkbench.BuildCanonical(member,id,revision,lines);
        return preview.State!=SyntheticCostPreviewState.Invalid;
    }
    private static bool TryCommand(JsonElement v,Guid routeId,out CostAwarenessCommand command)
    {
        command=default;
        if(!Keys(v,"requestId","expectedRevision","memberRef","previewId",
                "previewRevision","previewFingerprint","choice") ||
            !Uuid(v.GetProperty("requestId"),out var request) ||
            !Int(v.GetProperty("expectedRevision"),out var rev) ||
            !Str(v.GetProperty("memberRef"),out var member) ||
            !Uuid(v.GetProperty("previewId"),out var id) || id!=routeId ||
            !Int(v.GetProperty("previewRevision"),out var previewRevision) ||
            !Str(v.GetProperty("previewFingerprint"),out var fingerprint) ||
            !Str(v.GetProperty("choice"),out var rawChoice) ||
            !SyntheticCostPreviewBuilder.Synthetic(member) ||
            !Enum.TryParse<CostAwarenessChoice>(rawChoice,false,out var choice) ||
            !Enum.IsDefined(choice)||rawChoice!=choice.ToString())return false;
        command=new(request,rev,member!,id,previewRevision,fingerprint!,choice);
        return true;
    }
}
