using System.Text.Json;
using Taranom.MemberGovernance;

namespace Taranom.OperationsStatus;

// Explicit opt-in local test adapter. Not member authentication or a financial API.
// No database, storage, pricing, insurer, wallet, or clinical operations.
internal static class SyntheticCostWorkbenchHttp
{
    internal const string Path = "/internal/ops/v1/synthetic-cost-workbench";
    private const string Contract = "TRN-TECH-093";
    internal static async Task<IResult> Inspect(HttpContext context)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_SYNTHETIC_COST_WORKBENCH") !=
            "CI_EPHEMERAL_ONLY")
            return Results.NotFound();

        var req = context.Request;
        var origin = req.Headers.Origin.ToString();
        if (origin != "http://127.0.0.1:4318" && origin != "http://localhost:4318")
            return Denied();
        if (req.ContentType is null ||
            !req.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            req.ContentLength is > 8192)
            return Denied();
        try
        {
            var buffer = new byte[8193];
            var length = 0;
            while (length < buffer.Length)
            {
                var count = await req.Body.ReadAsync(buffer.AsMemory(length));
                if (count == 0) break;
                length += count;
            }
            if (length is < 2 or > 8192) return Denied();
            using var doc = JsonDocument.Parse(buffer.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = 5 });
            var root = doc.RootElement;
            if (!Keys(root, "memberRef", "previewId", "previewRevision", "lines") ||
                !Text(root.GetProperty("memberRef"), out var memberRef) ||
                !Text(root.GetProperty("previewId"), out var idText) ||
                !Guid.TryParseExact(idText, "D", out var previewId) ||
                !Number(root.GetProperty("previewRevision"), out var revision) ||
                root.GetProperty("lines").ValueKind != JsonValueKind.Array)
                return Denied();

            var raw = root.GetProperty("lines");
            if (raw.GetArrayLength() != 5) return Denied();
            var lines = new List<SyntheticCostWorkbenchLine>(5);
            foreach (var item in raw.EnumerateArray())
            {
                if (!Keys(item, "component", "state", "amount", "currency",
                        "sourceRef", "evidenceRef", "sourceRevision") ||
                    !Text(item.GetProperty("component"), out var component) ||
                    !Text(item.GetProperty("state"), out var state) ||
                    !Text(item.GetProperty("amount"), out var amount) ||
                    !Text(item.GetProperty("currency"), out var currency) ||
                    !Text(item.GetProperty("sourceRef"), out var sourceRef) ||
                    !Text(item.GetProperty("evidenceRef"), out var evidenceText) ||
                    !Number(item.GetProperty("sourceRevision"), out var sourceRevision))
                    return Denied();
                Guid? evidenceRef = null;
                if (evidenceText is not null)
                {
                    if (!Guid.TryParseExact(evidenceText, "D", out var parsedEvidence))
                        return Denied();
                    evidenceRef = parsedEvidence;
                }
                lines.Add(new(component, state, amount, currency, sourceRef,
                    evidenceRef, sourceRevision));
            }
            var result = SyntheticCostWorkbench.Inspect(memberRef, previewId, revision, lines);
            if (result.State == "Invalid") return Denied();
            return Results.Json(new
            {
                contract = Contract,
                syntheticOnly = true,
                memberAuthenticated = false,
                officialEstimateVerified = false,
                insurerEntitlementVerified = false,
                choiceCarriedForward = false,
                financialCommitmentCreated = false,
                clinicalConsentGranted = false,
                walletMutated = false,
                nationalReleaseAllowed = false,
                state = result.State,
                previewId,
                previewRevision = revision,
                fingerprint = result.Fingerprint,
                lines = result.Lines
            });
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
        {
            return Denied();
        }
    }

    private static bool Keys(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var properties = element.EnumerateObject().Select(p => p.Name).ToArray();
        return properties.Length == names.Length &&
            properties.Distinct(StringComparer.Ordinal).Count() == names.Length &&
            properties.ToHashSet(StringComparer.Ordinal).SetEquals(names);
    }

    private static bool Text(JsonElement element, out string? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return true;
    }

    private static bool Number(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    private static IResult Denied() => Results.Json(new
    {
        contract = Contract,
        outcome = "INVALID_SYNTHETIC_COST_INPUT",
        syntheticOnly = true,
        memberAuthenticated = false,
        officialEstimateVerified = false,
        insurerEntitlementVerified = false,
        choiceCarriedForward = false,
        financialCommitmentCreated = false,
        clinicalConsentGranted = false,
        walletMutated = false,
        nationalReleaseAllowed = false
    }, statusCode: 400);
}
