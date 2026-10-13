using System.Security.Cryptography;
using System.Text.Json;

namespace Taranom.OperationsStatus;

// TRN-TECH-116: Strict projection of the SAME two repository DRAFT sources
// already checked by canonical Python TRN-TECH-069 in 14-gate CI.
// Never synthesize formal acceptance, % Done, CI pass or real external evidence.
internal sealed record ProgressCap(string Id,string Title,string EvidenceClass,
    string? CodeRef,string? TestRef,bool FormallyAccepted);
internal sealed record ProgressEpic(string Id,string Label,
    ProgressCap[] Capabilities,string[] MissingExternalEvidence);
internal sealed record ProgressInventory(string SourceFingerprint,
    ProgressEpic[] Epics,int LinkedTestOnlyPairs,int UnlinkedCandidates);

internal static class EpicProgressHttp
{
    private static readonly string[] Classes =
        ["SYNTHETIC_INTERNAL","DISCONNECTED_UI_ONLY","OFFLINE_POLICY_ONLY","NO_EVIDENCE"];
    private static readonly string[] OtherDimensions =
        ["MARKET-PUBLIC","MARKET-SUPPORT","MARKET-CORPORATE","NATIONAL-COVERAGE"];

    internal static IResult Read(HttpContext _)
    {
        var root=Environment.GetEnvironmentVariable("TARANOM_OPS_REPO_ROOT");
        var denied=ReleaseDenialSource.Read(root);
        var inventory=EpicProgressSource.Read(root,denied);
        if(inventory is null)
            return Results.Json(new{
                contract="TRN-TECH-116",status="SOURCE_UNVERIFIABLE",
                scope="DRAFT_UNAPPROVED",nationalReleaseAllowed=false,
                overallImplementationCompletionPct=(int?)null,
                formalAcceptancePct=(int?)null,
                epics=Array.Empty<object>(),blockedDimensions=Array.Empty<object>()
            },statusCode:503);
        return Results.Json(new{
            contract="TRN-TECH-116",status="DRAFT_EVIDENCE_CATALOG_ONLY",
            scope="DRAFT_UNAPPROVED",weightsApproved=false,
            officialAcceptanceObserved=false,productionReady=false,
            nationalReleaseAllowed=false,releaseAdmission="DENIED_BY_DEFAULT",
            overallImplementationCompletionPct=(int?)null,
            formalAcceptancePct=(int?)null,
            epicCount=13,provisionalCapabilities=39,
            linkedNonProductionCodeTestPairs=inventory.LinkedTestOnlyPairs,
            candidatesWithoutReferences=inventory.UnlinkedCandidates,
            externalBlockersUnverified=17,
            sourceFingerprint=inventory.SourceFingerprint,
            epics=inventory.Epics,
            blockedDimensions=denied
        });
    }
}
internal static class EpicProgressSource
{
    private const int Limit=131072;
    private static readonly string[] OtherDimensions =
        ["MARKET-PUBLIC","MARKET-SUPPORT","MARKET-CORPORATE","NATIONAL-COVERAGE"];
    private static readonly string[] Classes =
        ["SYNTHETIC_INTERNAL","DISCONNECTED_UI_ONLY","OFFLINE_POLICY_ONLY","NO_EVIDENCE"];

    internal static ProgressInventory? Read(string? path,Dimension[]? dimensions)
    {
        if(string.IsNullOrWhiteSpace(path)||dimensions is not {Length:17})return null;
        try
        {
            var root=Path.GetFullPath(path);
            var regBytes=Source(root,"docs/governance/epic-capability-progress-draft.json");
            var blockerBytes=Source(root,"docs/governance/national-launch-blockers.json");
            using var registry=JsonDocument.Parse(regBytes);
            using var blockers=JsonDocument.Parse(blockerBytes);
            var a=registry.RootElement;
            var b=blockers.RootElement;
            if(!Fields(a,["schema_version","scope","baseline_status","weights_status",
                           "method","capabilities"]) ||
               a.GetProperty("schema_version").GetInt32()!=1 ||
               Str(a,"scope")!="COMPREHENSIVE_NATIONAL_PRODUCT_EPIC_MEASUREMENT" ||
               Str(a,"baseline_status")!="DRAFT_UNAPPROVED" ||
               Str(a,"weights_status")!="NOT_APPROVED" ||
               Str(a,"method")!="THREE_CANDIDATE_ACCEPTANCE_CHECKS_PER_EPIC_NOT_FINAL_SCOPE" ||
               Str(b,"release_admission")!="DENIED_BY_DEFAULT" ||
               b.GetProperty("real_release_approval_observed").ValueKind!=JsonValueKind.False ||
               b.GetProperty("external_evidence_verified").ValueKind!=JsonValueKind.False)
                return null;
            var blocks=b.GetProperty("items");
            if(blocks.ValueKind!=JsonValueKind.Array||blocks.GetArrayLength()!=17)
                return null;
            var byId=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
            foreach(var item in blocks.EnumerateArray())
            {
                if(!Fields(item,["id","label","status","policy_refs","missing_evidence"]))
                    return null;
                var id=Str(item,"id");
                if(id is null||!byId.TryAdd(id,item)||Str(item,"status")!="BLOCKED_UNVERIFIED"||
                   !dimensions.Any(x=>x.Id==id&&x.Status=="BLOCKED_UNVERIFIED"))
                    return null;
            }
            var expected=Enumerable.Range(1,13)
                .Select(i=>$"TRN-EP-{i:00}").Concat(OtherDimensions).ToHashSet(StringComparer.Ordinal);
            if(!byId.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
                return null;
            var items=a.GetProperty("capabilities");
            if(items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()!=39)
                return null;
            var groups=new Dictionary<string,List<ProgressCap>>(StringComparer.Ordinal);
            var used=new HashSet<string>(StringComparer.Ordinal);
            var linked=0;var unlinked=0;
            foreach(var cap in items.EnumerateArray())
            {
                if(!Fields(cap,["epic_id","capability_id","capability","decision_refs",
                                "implementation_ref","test_ref","evidence_class",
                                "formal_acceptance_observed"])||
                   cap.GetProperty("formal_acceptance_observed").ValueKind!=JsonValueKind.False)
                    return null;
                var epic=Str(cap,"epic_id");var id=Str(cap,"capability_id");
                var title=Str(cap,"capability");var kind=Str(cap,"evidence_class");
                if(epic is null||!expected.Contains(epic)||!epic.StartsWith("TRN-EP-",StringComparison.Ordinal)||
                   id is null||!id.StartsWith("TRN-CAP-"+epic[7..]+"-",StringComparison.Ordinal)||
                   id.Length!=13||id[^2..] is not ("01" or "02" or "03")||
                   !used.Add(id)||string.IsNullOrWhiteSpace(title)||title.Length>180||
                   kind is null||!Classes.Contains(kind,StringComparer.Ordinal))
                    return null;
                var decision=cap.GetProperty("decision_refs");
                var policies=byId[epic].GetProperty("policy_refs");
                if(decision.ValueKind!=JsonValueKind.Array||
                   policies.ValueKind!=JsonValueKind.Array||
                   !decision.EnumerateArray().Select(x=>x.GetString())
                       .SequenceEqual(policies.EnumerateArray().Select(x=>x.GetString())))
                    return null;
                string? code=NullableStr(cap,"implementation_ref");
                string? test=NullableStr(cap,"test_ref");
                if(kind=="NO_EVIDENCE")
                {
                    if(code is not null||test is not null)return null;
                    unlinked++;
                }
                else
                {
                    if(code is null||test is null||
                       !EvidenceRef(root,code,["src/","apps/","tools/"])||
                       !EvidenceRef(root,test,["tests/","apps/member-web/tests/","apps/center-web/tests/"])||
                       (kind=="SYNTHETIC_INTERNAL"&&!code.StartsWith("src/",StringComparison.Ordinal))||
                       (kind=="DISCONNECTED_UI_ONLY"&&!code.StartsWith("apps/",StringComparison.Ordinal))||
                       (kind=="OFFLINE_POLICY_ONLY"&&!code.StartsWith("tools/",StringComparison.Ordinal)))
                        return null;
                    linked++;
                }
                if(!groups.TryGetValue(epic,out var collection))
                {
                    collection=[];
                    groups.Add(epic,collection);
                }
                collection.Add(new ProgressCap(id,title,kind,code,test,false));
            }
            var epics=new List<ProgressEpic>();
            for(var number=1;number<=13;number++)
            {
                var id=$"TRN-EP-{number:00}";
                if(!groups.TryGetValue(id,out var caps)||caps.Count!=3||
                   !caps.Select(x=>x.Id).ToHashSet(StringComparer.Ordinal)
                      .SetEquals(Enumerable.Range(1,3).Select(n=>$"TRN-CAP-{number:00}-{n:00}")))
                    return null;
                var d=byId[id];
                var missing=d.GetProperty("missing_evidence");
                if(missing.ValueKind!=JsonValueKind.Array||missing.GetArrayLength()==0)
                    return null;
                epics.Add(new ProgressEpic(id,Str(d,"label")??id,
                    caps.OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray(),
                    missing.EnumerateArray().Select(x=>x.GetString()??"").ToArray()));
            }
            if(linked+unlinked!=39 || dimensions.Any(x=>!byId.ContainsKey(x.Id)))return null;
            var hash=SHA256.HashData(regBytes.Concat(blockerBytes).ToArray());
            return new ProgressInventory(Convert.ToHexString(hash).ToLowerInvariant(),
                epics.ToArray(),linked,unlinked);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or ArgumentException or
            OverflowException or KeyNotFoundException or System.Security.SecurityException)
        {
            return null;
        }
    }
    private static byte[] Source(string root,string relative)
    {
        if(!EvidenceRef(root,relative,["docs/governance/"]))
            throw new InvalidOperationException("SOURCE_UNSAFE");
        var file=new FileInfo(Path.Combine(root,relative));
        if(file.Length is <1 or >Limit)throw new InvalidOperationException("SOURCE_SIZE");
        return File.ReadAllBytes(file.FullName);
    }
    private static bool EvidenceRef(string root,string name,string[] prefixes)
    {
        if(name.Length>250||!prefixes.Any(name.StartsWith)||
           !(name.EndsWith(".cs",StringComparison.Ordinal)||
             name.EndsWith(".py",StringComparison.Ordinal)||
             name.EndsWith(".tsx",StringComparison.Ordinal)||
             name.EndsWith(".ts",StringComparison.Ordinal)||
             name.EndsWith(".json",StringComparison.Ordinal))||
           name.Contains('\\')||name.Split('/').Any(p=>p is "" or "." or ".."))
            return false;
        var absolute=Path.GetFullPath(Path.Combine(root,name));
        if(!absolute.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            return false;
        var current=root;
        foreach(var part in name.Split('/'))
        {
            current=Path.Combine(current,part);
            var attrs=File.GetAttributes(current);
            if((attrs&FileAttributes.ReparsePoint)!=0)return false;
        }
        return File.Exists(absolute);
    }
    private static bool Fields(JsonElement x,string[] names)
    {
        if(x.ValueKind!=JsonValueKind.Object)return false;
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in x.EnumerateObject())
            if(!seen.Add(p.Name)||!names.Contains(p.Name,StringComparer.Ordinal))
                return false;
        return seen.Count==names.Length;
    }
    private static string? Str(JsonElement item,string name)
    {
        var x=item.GetProperty(name);
        return x.ValueKind==JsonValueKind.String?x.GetString():null;
    }
    private static string? NullableStr(JsonElement item,string name)
    {
        var x=item.GetProperty(name);
        if(x.ValueKind==JsonValueKind.Null)return null;
        if(x.ValueKind!=JsonValueKind.String)
            throw new InvalidOperationException("INVALID_REF");
        return x.GetString();
    }
}
