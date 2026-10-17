using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Taranom.OperationsStatus;

// TRN-TECH-165: real localhost HTTP projection of the same two DRAFT source
// files and guard used by TRN-TECH-116 / TRN-TECH-164. This is NOT an approval
// workflow, credentialed identity, independent evidence or Production endpoint.
internal sealed record ReleaseDependencyRow(
    string Requirement,string[] BlockedDomainIds,string[] AffectedEpicIds,
    string[] AffectedMarketOrNationalIds,string[] PolicyRefs,
    int LinkedNonProductionCandidatePairsInAffectedEpics,string DecisionOwner,
    string EvidenceStatus,string HumanDecisionStatus,bool FormalAcceptanceObserved);

internal sealed record EngineeringCandidate(
    string Id,string EpicId,string Title,string EvidenceClass,string? CodeRef,
    string? TestRef,string EvidenceLane,string[] EpicMissingExternalEvidence,
    string[] RelatedUnverifiedRequirements,bool FormalAcceptanceObserved,
    string EngineeringReadiness);

internal static class ReleaseDependencyHttp
{
    private const string Contract="TRN-TECH-165";
    private static readonly string[] Required = [
        "ALL_3_MARKET_OPERATIONS","AUTHENTIC_MEMBERSHIP_AND_ACTIVATION",
        "CLINICAL_SAFETY_AND_RESPONSIBILITY","COMPLAINTS_LEGAL_AUTHORITY_AND_PROCESS",
        "CONSENTED_AI_DATASET_AND_INDEPENDENT_EVALUATION","DATA_RIGHTS_AND_CONSENT",
        "EMERGENCY_PROTOCOL_AND_CAPACITY","FINANCIAL_LICENSE_BACKING_AND_AUDIT",
        "HUMAN_AI_COMMITTEE_AND_RELEASE_APPROVAL","INDEPENDENT_REVIEWS_AND_RELEASE_APPROVAL",
        "INSURER_AGREEMENT_AND_ENTITLEMENT","LEGAL_OPERATOR_IDENTITY",
        "NATIONAL_COVERAGE_EVIDENCE","OFFICIAL_BILLING_AND_MEMBER_ACKNOWLEDGMENT",
        "PRODUCTION_HOSTING_AND_SECURITY","PROVIDER_LICENSE_AND_CONTRACT",
        "REAL_EXTERNAL_INTEGRATIONS","VERIFIED_MEMBER_IDENTITY"
    ];

    internal static IResult Read(HttpContext _)
    {
        var source=TryRead(Environment.GetEnvironmentVariable("TARANOM_OPS_REPO_ROOT"));
        if(source is null)
            return Results.Json(new {
                contract=Contract,status="SOURCE_UNVERIFIABLE",
                releaseAdmission="DENIED_BY_DEFAULT",formalApprovalObserved=false,
                productionReady=false,nationalReleaseAllowed=false,
                blockedDomains=0,candidateCapabilities=0,requiredOwnersUnassigned=0,
                workItems=Array.Empty<object>(),
                engineeringCandidates=Array.Empty<object>(),
                technicalReferencesPresent=0,technicalReferencesMissing=0
            },statusCode:StatusCodes.Status503ServiceUnavailable);
        return Results.Json(new {
            contract=Contract,status="DRAFT_UNVERIFIED_DEPENDENCY_WORKLIST",
            releaseAdmission="DENIED_BY_DEFAULT",formalApprovalObserved=false,
            productionReady=false,nationalReleaseAllowed=false,
            blockedDomains=17,candidateCapabilities=39,requiredOwnersUnassigned=18,
            sourceFingerprint=source.Value.Fingerprint,workItems=source.Value.Rows,
            engineeringCandidates=source.Value.Candidates,
            technicalReferencesPresent=26,technicalReferencesMissing=13
        });
    }

    private static (string Fingerprint,ReleaseDependencyRow[] Rows,
        EngineeringCandidate[] Candidates)? TryRead(string? repo)
    {
        if(string.IsNullOrWhiteSpace(repo))return null;
        try
        {
            var dimensions=ReleaseDenialSource.Read(repo);
            var inventory=EpicProgressSource.Read(repo,dimensions);
            if(dimensions is not {Length:17}||inventory is null||
                inventory.Epics.Length!=13)return null;
            var root=Path.GetFullPath(repo);
            var r=CheckedRead(root,"docs/governance/epic-capability-progress-draft.json");
            var b=CheckedRead(root,"docs/governance/national-launch-blockers.json");
            var fingerprint=Convert.ToHexString(SHA256.HashData(r.Concat(b).ToArray())).ToLowerInvariant();
            if(!string.Equals(fingerprint,inventory.SourceFingerprint,StringComparison.Ordinal))
                return null; // deny file drift between the two separate guarded readers
            using var doc=JsonDocument.Parse(b,new JsonDocumentOptions{MaxDepth=8});
            var block=doc.RootElement;
            if(!HasFields(block,["schema_version","scope","source_policy_refs",
                "release_admission","real_release_approval_observed",
                "external_evidence_verified","statement","items"])||
                block.GetProperty("schema_version").GetInt32()!=1||
                block.GetProperty("release_admission").GetString()!="DENIED_BY_DEFAULT"||
                block.GetProperty("real_release_approval_observed").ValueKind!=JsonValueKind.False||
                block.GetProperty("external_evidence_verified").ValueKind!=JsonValueKind.False)
                return null;
            var items=block.GetProperty("items");
            if(items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()!=17)return null;
            var verified=dimensions.ToDictionary(x=>x.Id,StringComparer.Ordinal);
            var domainPolicies=new Dictionary<string,string[]>(StringComparer.Ordinal);
            var group=new Dictionary<string,List<string>>(StringComparer.Ordinal);
            foreach(var d in items.EnumerateArray())
            {
                if(!HasFields(d,["id","label","status","policy_refs","missing_evidence"]))return null;
                var id=d.GetProperty("id").GetString();
                if(id is null||!verified.TryGetValue(id,out var original)||
                    domainPolicies.ContainsKey(id)||
                    d.GetProperty("label").GetString()!=original.Label||
                    d.GetProperty("status").GetString()!="BLOCKED_UNVERIFIED")return null;
                var missing=ReadStrings(d.GetProperty("missing_evidence"));
                var policies=ReadStrings(d.GetProperty("policy_refs"));
                if(missing is null||policies is null||missing.Length==0||
                    policies.Length==0||!missing.SequenceEqual(original.MissingEvidence)||
                    policies.Any(x=>!Regex.IsMatch(x,@"\ATRN-BIZ-[0-9]{3}\z"))||
                    policies.Length!=policies.Distinct(StringComparer.Ordinal).Count())
                    return null;
                domainPolicies.Add(id,policies);
                foreach(var requirement in missing)
                {
                    if(!Required.Contains(requirement,StringComparer.Ordinal))return null;
                    if(!group.TryGetValue(requirement,out var ids))group[requirement]=ids=[];
                    ids.Add(id);
                }
            }
            if(verified.Count!=domainPolicies.Count||
               group.Count!=Required.Length||
               !group.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Required))
                return null;
            var rows=new List<ReleaseDependencyRow>();
            foreach(var category in Required.OrderBy(x=>x,StringComparer.Ordinal))
            {
                var ids=group[category].OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                var epics=ids.Where(x=>x.StartsWith("TRN-EP-",StringComparison.Ordinal)).ToArray();
                var markets=ids.Where(x=>!x.StartsWith("TRN-EP-",StringComparison.Ordinal)).ToArray();
                var refs=ids.SelectMany(x=>domainPolicies[x])
                    .Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                var linked=inventory.Epics.Where(x=>epics.Contains(x.Id,StringComparer.Ordinal))
                    .Sum(x=>x.Capabilities.Count(c=>c.EvidenceClass!="NO_EVIDENCE"));
                rows.Add(new ReleaseDependencyRow(category,ids,epics,markets,refs,
                    linked,"NOT_ASSIGNED","BLOCKED_UNVERIFIED","NOT_OBSERVED",false));
            }
            if(rows.Count!=18 || rows.Sum(x=>x.BlockedDomainIds.Length)<17)return null;
            var candidates=new List<EngineeringCandidate>();
            foreach(var epic in inventory.Epics.OrderBy(x=>x.Id,StringComparer.Ordinal))
            {
                var related=rows.Where(x=>x.AffectedEpicIds.Contains(epic.Id,StringComparer.Ordinal))
                    .Select(x=>x.Requirement).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                if(related.Length==0||epic.MissingExternalEvidence.Length==0)return null;
                foreach(var cap in epic.Capabilities.OrderBy(x=>x.Id,StringComparer.Ordinal))
                {
                    var missing=cap.EvidenceClass=="NO_EVIDENCE";
                    if(missing!=(cap.CodeRef is null&&cap.TestRef is null))return null;
                    candidates.Add(new EngineeringCandidate(cap.Id,epic.Id,cap.Title,
                        cap.EvidenceClass,cap.CodeRef,cap.TestRef,
                        missing?"MISSING_CODE_TEST_REFERENCE":"NONPRODUCTION_REFERENCE_ONLY",
                        epic.MissingExternalEvidence,related,false,"NOT_ESTABLISHED"));
                }
            }
            if(candidates.Count!=39||
               candidates.Count(x=>x.EvidenceLane=="MISSING_CODE_TEST_REFERENCE")!=13||
               candidates.Count(x=>x.EvidenceLane=="NONPRODUCTION_REFERENCE_ONLY")!=26||
               candidates.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=39)
                return null;
            return (fingerprint,rows.ToArray(),candidates.ToArray());
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or OverflowException or System.Security.SecurityException)
        { return null; }
    }

    private static byte[] CheckedRead(string root,string relative)
    {
        var current=root;
        foreach(var name in relative.Split('/'))
        {
            current=Path.Combine(current,name);
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidOperationException("UNSAFE_SOURCE");
        }
        var file=new FileInfo(current);
        if(!file.Exists||file.LinkTarget is not null||file.Length is <1 or >131072)
            throw new InvalidOperationException("INVALID_SOURCE");
        return File.ReadAllBytes(current);
    }

    private static string[]? ReadStrings(JsonElement source)
    {
        if(source.ValueKind!=JsonValueKind.Array||source.GetArrayLength() is <1 or >25)return null;
        var strings=new List<string>();
        foreach(var value in source.EnumerateArray())
        {
            if(value.ValueKind!=JsonValueKind.String)return null;
            var s=value.GetString();
            if(string.IsNullOrWhiteSpace(s)||s.Length>150||
                strings.Contains(s,StringComparer.Ordinal))return null;
            strings.Add(s);
        }
        return strings.ToArray();
    }

    private static bool HasFields(JsonElement value,string[] expected)
    {
        if(value.ValueKind!=JsonValueKind.Object)return false;
        var found=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())
            if(!found.Add(property.Name)||!expected.Contains(property.Name,StringComparer.Ordinal))
                return false;
        return found.Count==expected.Length;
    }
}
