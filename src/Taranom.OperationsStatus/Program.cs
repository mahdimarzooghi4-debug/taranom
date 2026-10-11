using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
// Developer-only same-origin end-to-end probe. Never expose to public network.
builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(4318));
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'none'; base-uri 'none'; object-src 'none'; frame-src 'none'; " +
        "form-action 'none'; script-src 'self'; style-src 'self'; img-src 'self'; " +
        "connect-src 'self'; font-src 'self'";
    await next();
});
app.MapGet("/internal/ops/v1/status", () =>
{
    var inventory = ReleaseDenialSource.Read(Environment.GetEnvironmentVariable("TARANOM_OPS_REPO_ROOT"));
    if (inventory is null)
        return Results.Json(new
        {
            contract = "TRN-TECH-071",
            runtime = "LOOPBACK_PROCESS_ALIVE",
            inventory = "SOURCE_UNVERIFIABLE",
            releaseAllowed = false,
            verifiedIntegrations = false,
            dimensions = Array.Empty<object>()
        }, statusCode: 503);
    return Results.Json(new
    {
        contract = "TRN-TECH-071",
        runtime = "LOOPBACK_PROCESS_ALIVE",
        inventory = "SOURCE_BLOCKED_UNVERIFIED",
        releaseAllowed = false,
        verifiedIntegrations = false,
        dimensions = inventory
    });
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

internal sealed record Dimension(string Id, string Label, string Status, string[] MissingEvidence);

internal static class ReleaseDenialSource
{
    private static readonly HashSet<string> Ids = new(
        Enumerable.Range(1, 13).Select(n => $"TRN-EP-{n:00}")
            .Concat(["MARKET-PUBLIC", "MARKET-SUPPORT", "MARKET-CORPORATE", "NATIONAL-COVERAGE"]),
        StringComparer.Ordinal);

    internal static Dimension[]? Read(string? repo)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;
        try
        {
            var file = new FileInfo(Path.Combine(repo, "docs", "governance",
                "national-launch-blockers.json"));
            if (!file.Exists || file.LinkTarget is not null || file.Length is < 1 or > 131072)
                return null;
            using var document = JsonDocument.Parse(File.ReadAllBytes(file.FullName));
            var root = document.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1 ||
                root.GetProperty("release_admission").GetString() != "DENIED_BY_DEFAULT" ||
                root.GetProperty("external_evidence_verified").ValueKind != JsonValueKind.False ||
                root.GetProperty("real_release_approval_observed").ValueKind != JsonValueKind.False)
                return null;
            var items = root.GetProperty("items");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != 17)
                return null;
            var observed = new HashSet<string>(StringComparer.Ordinal);
            var dimensions = new List<Dimension>();
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                var label = item.GetProperty("label").GetString();
                var status = item.GetProperty("status").GetString();
                var missing = item.GetProperty("missing_evidence");
                if (id is null || !Ids.Contains(id) || !observed.Add(id) ||
                    string.IsNullOrWhiteSpace(label) || label.Length > 120 ||
                    status != "BLOCKED_UNVERIFIED" ||
                    missing.ValueKind != JsonValueKind.Array ||
                    missing.GetArrayLength() is < 1 or > 20)
                    return null;
                var codes = new List<string>();
                foreach (var entry in missing.EnumerateArray())
                {
                    var code = entry.GetString();
                    if (string.IsNullOrWhiteSpace(code) || code.Length > 100 ||
                        !code.All(c => char.IsAsciiLetterUpper(c) || c == '_') ||
                        codes.Contains(code, StringComparer.Ordinal))
                        return null;
                    codes.Add(code);
                }
                dimensions.Add(new Dimension(id, label, status, codes.ToArray()));
            }
            return observed.SetEquals(Ids) ? dimensions.ToArray() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or JsonException or KeyNotFoundException or InvalidOperationException
            or ArgumentException or OverflowException)
        {
            // Do not reveal actual path, exception, file contents, or private data.
            return null;
        }
    }
}
