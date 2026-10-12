using System.Text.Json;
using System.Globalization;
using Taranom.MemberGovernance;

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
    // Strict loopback HTTP authority; a local listener without Host checking
    // is vulnerable to browser DNS-rebinding to a developer-only service.
    // A future trusted reverse proxy has not been approved for this app.
    var authority = context.Request.Host;
    var hostAllowed = authority.Port == 4318 &&
        (string.Equals(authority.Host, "127.0.0.1", StringComparison.Ordinal) ||
         string.Equals(authority.Host, "localhost", StringComparison.OrdinalIgnoreCase));
    var untrustedProxy = context.Request.Headers.ContainsKey("Forwarded") ||
        context.Request.Headers.ContainsKey("X-Forwarded-Host") ||
        context.Request.Headers.ContainsKey("X-Forwarded-Proto") ||
        context.Request.Headers.ContainsKey("X-Forwarded-For");
    if (!hostAllowed || untrustedProxy)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            contract = "TRN-TECH-078",
            outcome = "UNTRUSTED_LOOPBACK_AUTHORITY",
            syntheticOnly = true,
            verifiedIntegrations = false,
            officialActivationVerified = false,
            walletCreditAuthorized = false,
            releaseAllowed = false,
            nationalReleaseAllowed = false
        });
        return;
    }
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

// Explicitly anonymous and hypothetical: an actual member's activation,
// verified event source, legal status and wallet eligibility are NEVER inferred.
app.MapPost("/internal/ops/v1/waiting-preview",
    (Func<HttpContext, Task<IResult>>)WaitingPreview.Evaluate);

app.UseDefaultFiles();
app.UseStaticFiles();
// Explicit technical UI entrypoint. Unknown URLs do not become a 200 HTML
// response or impersonate a member login / operational backend endpoint.
app.MapGet("/", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
        "index.html"), "text/html; charset=utf-8"));
app.MapGet("/operations", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
        "index.html"), "text/html; charset=utf-8"));
// Keep HTTP method mismatch distinct from a missing route. The former must
// remain 405; the latter cannot be a successful SPA HTML page.
app.MapFallback((Func<HttpContext, IResult>)OpsFallback.Evaluate);
app.Run();

internal static class OpsFallback
{
    internal static IResult Evaluate(HttpContext context)
    {
        var path = context.Request.Path.Value;
        var method = context.Request.Method;
        if ((path == "/internal/ops/v1/status" && !HttpMethods.IsGet(method)) ||
            (path == "/internal/ops/v1/waiting-preview" && !HttpMethods.IsPost(method)) ||
            (path == "/operations" && !HttpMethods.IsGet(method)))
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        return Results.NotFound();
    }
}

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
                        !code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_') ||
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


// Public historical rule demonstration only. No identity, session, stored
// dates, claim of official activation or membership entitlement; no side effects.
internal static class WaitingPreview
{
    private const string Contract = "TRN-TECH-072";
    private const string IsoUtc = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    internal static async Task<IResult> Evaluate(HttpContext context)
    {
        var req = context.Request;
        if (req.Headers.Origin.Count > 0 &&
            !string.Equals(req.Headers.Origin.ToString(),
                "http://127.0.0.1:4318", StringComparison.Ordinal) &&
            !string.Equals(req.Headers.Origin.ToString(),
                "http://localhost:4318", StringComparison.Ordinal))
            return Invalid();
        if (req.ContentType is null ||
            !req.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            req.ContentLength is > 512)
            return Invalid();

        // Enforce body bound even when Content-Length is omitted/chunked.
        var buffer = new byte[513];
        var count = 0;
        try
        {
            while (count < buffer.Length)
            {
                var read = await req.Body.ReadAsync(buffer.AsMemory(count));
                if (read == 0) break;
                count += read;
            }
            if (count is < 2 or > 512) return Invalid();
            using var doc = JsonDocument.Parse(buffer.AsMemory(0, count),
                new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("activationUtc", out var rawActivation) ||
                !root.TryGetProperty("observedUtc", out var rawObserved) ||
                rawObserved.ValueKind != JsonValueKind.String ||
                rawActivation.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                return Invalid();

            var observedText = rawObserved.GetString();
            if (!TryUtc(observedText, out var observed)) return Invalid();

            DateTimeOffset? activation = null;
            if (rawActivation.ValueKind == JsonValueKind.String)
            {
                if (!TryUtc(rawActivation.GetString(), out var at)) return Invalid();
                activation = at;
            }

            var projection = MembershipWaitingPeriod.Observe(activation, observed);
            return Results.Json(new
            {
                contract = Contract,
                hypotheticalOnly = true,
                officialActivationVerified = false,
                memberEligibilityVerified = false,
                walletCreditAuthorized = false,
                nationalReleaseAllowed = false,
                state = projection.State.ToString(),
                thresholdUtc = projection.ThresholdUtc?.ToString(
                    IsoUtc, CultureInfo.InvariantCulture)
            });
        }
        catch (JsonException)
        {
            return Invalid();
        }
        catch (IOException)
        {
            return Invalid();
        }
    }

    private static bool TryUtc(string? value, out DateTimeOffset utc)
    {
        utc = default;
        return value is { Length: 20 } &&
            DateTimeOffset.TryParseExact(value, IsoUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utc);
    }

    private static IResult Invalid() => Results.Json(new
    {
        contract = Contract,
        status = "INVALID_HYPOTHETICAL_INPUT",
        hypotheticalOnly = true,
        officialActivationVerified = false,
        memberEligibilityVerified = false,
        walletCreditAuthorized = false,
        nationalReleaseAllowed = false
    }, statusCode: 400);
}
