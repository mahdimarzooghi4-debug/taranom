using System.Text.Json;
using Taranom.MemberGovernance;

namespace Taranom.OperationsStatus;

// TRN-TECH-094: localhost opt-in ONLY, read/evaluate paper events with no storage.
// This is NOT a member choice API, official consent, financial authorization,
// signed acknowledgement, or record keeper.
internal static class SyntheticCostChoiceWorkbenchHttp
{
    internal const string Path = "/internal/ops/v1/synthetic-cost-choice";
    private const string Contract = "TRN-TECH-094";
    internal static async Task<IResult> Inspect(HttpContext context)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_SYNTHETIC_COST_WORKBENCH")
            != "CI_EPHEMERAL_ONLY") return Results.NotFound();
        var req = context.Request;
        var origin = req.Headers.Origin.ToString();
        if ((origin != "http://127.0.0.1:4318" && origin != "http://localhost:4318") ||
            req.ContentType is null ||
            !req.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            req.ContentLength is > 16384)
            return Denied();
        try
        {
            var buffer = new byte[16385];
            int count = 0;
            while (count < buffer.Length)
            {
                int read = await req.Body.ReadAsync(buffer.AsMemory(count));
                if (read == 0) break;
                count += read;
            }
            if (count is < 2 or > 16384) return Denied();
            using var doc = JsonDocument.Parse(buffer.AsMemory(0, count),
                new JsonDocumentOptions { MaxDepth = 6 });
            var root = doc.RootElement;
            if (!Keys(root, "previous", "current", "history", "attempt") ||
                !TryPreview(root.GetProperty("previous"), out var previous) ||
                !TryPreview(root.GetProperty("current"), out var current))
                return Denied();
            var historyElement = root.GetProperty("history");
            if (historyElement.ValueKind != JsonValueKind.Array ||
                historyElement.GetArrayLength() > 16) return Denied();
            var history = new List<SyntheticChoiceCommandInput>(16);
            foreach (var item in historyElement.EnumerateArray())
            {
                if (!TryCommand(item, out var cmd)) return Denied();
                history.Add(cmd!);
            }
            SyntheticChoiceCommandInput? attempt = null;
            var candidate = root.GetProperty("attempt");
            if (candidate.ValueKind != JsonValueKind.Null &&
                !TryCommand(candidate, out attempt))
                return Denied();
            var result = SyntheticCostChoiceWorkbench.Inspect(
                previous, current, history, attempt);
            if (result.Outcome == "Invalid") return Denied();
            return Results.Json(new
            {
                contract = Contract, syntheticOnly = true,
                result.Outcome, result.Continuity, result.HistoryRevision,
                result.LastUnverifiedChoice, result.ChangedComponents,
                result.ChoiceCarriedForward, result.FinancialCommitmentCreated,
                result.ClinicalConsentGranted, result.WalletMutated,
                result.MemberAuthenticated, result.NationalReleaseAllowed,
                officialChoiceRecorded = false, choiceSignatureValid = false
            });
        }
        catch (Exception error) when (error is JsonException or IOException or ArgumentException)
        {
            return Denied();
        }
    }

    private static bool TryPreview(JsonElement root, out SyntheticChoicePreviewInput? value)
    {
        value = null;
        if (!Keys(root, "memberRef", "previewId", "previewRevision", "lines") ||
            !Text(root.GetProperty("memberRef"), out var memberRef) ||
            !Id(root.GetProperty("previewId"), out var previewId) ||
            !Long(root.GetProperty("previewRevision"), out var revision) ||
            root.GetProperty("lines").ValueKind != JsonValueKind.Array ||
            root.GetProperty("lines").GetArrayLength() != 5)
            return false;
        var lines = new List<SyntheticCostWorkbenchLine>(5);
        foreach (var line in root.GetProperty("lines").EnumerateArray())
        {
            if (!Keys(line, "component", "state", "amount", "currency", "sourceRef",
                    "evidenceRef", "sourceRevision") ||
                !Text(line.GetProperty("component"), out var component) ||
                !Text(line.GetProperty("state"), out var state) ||
                !Text(line.GetProperty("amount"), out var amount) ||
                !Text(line.GetProperty("currency"), out var currency) ||
                !Text(line.GetProperty("sourceRef"), out var sourceRef) ||
                !NullableId(line.GetProperty("evidenceRef"), out var evidenceRef) ||
                !Long(line.GetProperty("sourceRevision"), out var sourceRevision))
                return false;
            lines.Add(new(component, state, amount, currency, sourceRef,
                evidenceRef, sourceRevision));
        }
        value = new(memberRef, previewId, revision, lines);
        return true;
    }

    private static bool TryCommand(JsonElement root, out SyntheticChoiceCommandInput? value)
    {
        value = null;
        if (!Keys(root, "requestId", "expectedRevision", "memberRef",
                "previewId", "previewRevision", "previewFingerprint", "choice") ||
            !Id(root.GetProperty("requestId"), out var requestId) ||
            !Long(root.GetProperty("expectedRevision"), out var expectedRevision) ||
            !Text(root.GetProperty("memberRef"), out var memberRef) ||
            !Id(root.GetProperty("previewId"), out var previewId) ||
            !Long(root.GetProperty("previewRevision"), out var previewRevision) ||
            !Text(root.GetProperty("previewFingerprint"), out var fingerprint) ||
            !Text(root.GetProperty("choice"), out var choice))
            return false;
        value = new(requestId, expectedRevision, memberRef, previewId,
            previewRevision, fingerprint, choice);
        return true;
    }

    private static bool Keys(JsonElement v, params string[] names)
    {
        if (v.ValueKind != JsonValueKind.Object) return false;
        var keys = v.EnumerateObject().Select(p => p.Name).ToArray();
        return keys.Length == names.Length &&
            keys.Distinct(StringComparer.Ordinal).Count() == names.Length &&
            keys.ToHashSet(StringComparer.Ordinal).SetEquals(names);
    }
    private static bool Text(JsonElement v, out string? s)
    {
        s = null;
        if (v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.String) return false;
        s = v.GetString();
        return true;
    }
    private static bool Id(JsonElement v, out Guid id)
    {
        id = Guid.Empty;
        return v.ValueKind == JsonValueKind.String &&
            Guid.TryParseExact(v.GetString(), "D", out id);
    }
    private static bool NullableId(JsonElement v, out Guid? id)
    {
        id = null;
        if (v.ValueKind == JsonValueKind.Null) return true;
        if (!Id(v, out var parsed)) return false;
        id = parsed;
        return true;
    }
    private static bool Long(JsonElement v, out long n)
    {
        n = 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out n);
    }
    private static IResult Denied() => Results.Json(new
    {
        contract = Contract, outcome = "INVALID_SYNTHETIC_CHOICE_INPUT",
        syntheticOnly = true, memberAuthenticated = false,
        officialChoiceRecorded = false, choiceSignatureValid = false,
        choiceCarriedForward = false, financialCommitmentCreated = false,
        clinicalConsentGranted = false, walletMutated = false,
        nationalReleaseAllowed = false
    }, statusCode: 400);
}
