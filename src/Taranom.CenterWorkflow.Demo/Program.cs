using System.Text.Json;
using Npgsql;
using Taranom.ClinicalCenterGovernance;
using Taranom.ClinicalCenterJournal.Postgres;
using Taranom.ProofingJournal.Postgres;

// TRN-TECH-073: ephemeral synthetic ONLY, never a real center management API.
if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") != "CI_EPHEMERAL_ONLY")
    throw new InvalidOperationException("TEST_ONLY_EXECUTION_REQUIRED");
var rawConnection = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
    ?? throw new InvalidOperationException("TEST_ONLY_DATABASE_REQUIRED");
var connection = new NpgsqlConnectionStringBuilder(rawConnection);
if (connection.Database != "taranom_sprint64_center" ||
    connection.Host is not ("localhost" or "127.0.0.1") ||
    connection.Port != 5432)
    throw new InvalidOperationException("ISOLATED_LOOPBACK_TEST_DATABASE_REQUIRED");

await using var dataSource = NpgsqlDataSource.Create(connection.ConnectionString);
await PgClinicalCenterJournal.InitializeIsolatedTestSchemaAsync(dataSource);
// TRN-TECH-113: a second isolated, source-verified synthetic journal.
// No real identity, national ID, licensed IdP or membership is involved.
await PgProofingCaseJournal.InitializeIsolatedTestSchemaAsync(dataSource);
// TRN-TECH-114: reuse existing immutable work-item journal for unverified
// historic-period evidence, never claim actual benefit approval or finance.
await PgLateEvidenceWorkItemJournal.InitializeIsolatedTestSchemaAsync(dataSource);
await Taranom.CenterWorkflow.Demo.PgSyntheticSupportJournal.InitializeAsync(dataSource);
// TRN-TECH-110: support HTTP must never inherit the schema-owner connection;
// the support journal gets only bounded DML on its three synthetic tables.
await using var supportRuntime = await Taranom.CenterWorkflow.Demo.PgSyntheticSupportJournal
    .CreateRestrictedTestRuntimeAsync(dataSource, connection);
// TRN-TECH-095: reuse the EXISTING synthetic cost-awareness tables and
// append-only event journal only when explicitly enabled in isolated CI.
if (Environment.GetEnvironmentVariable("TARANOM_COST_JOURNAL_DEMO") == "CI_EPHEMERAL_ONLY")
    await PgSyntheticCostAwarenessJournal.InitializeIsolatedTestSchemaAsync(dataSource);
var store = new PgClinicalCenterJournal(dataSource);
Taranom.CenterWorkflow.Demo.ProofingDemoHttp.Store =
    new PgProofingCaseJournal(dataSource);
Taranom.CenterWorkflow.Demo.LateEvidenceHttp.Store =
    new PgLateEvidenceWorkItemJournal(dataSource);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(4320));
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'none'; base-uri 'none'; form-action 'none'; " +
        "object-src 'none'; frame-src 'none'; script-src 'self'; " +
        "style-src 'self'; img-src 'self'; connect-src 'self'";
    // Browser same-origin is NOT authentication. This test-only server must
    // reject alternate Host values that could otherwise allow DNS rebinding.
    // No real reverse proxy or forwarded-header trust has been authorized.
    var host = context.Request.Host;
    var loopbackHost =
        (string.Equals(host.Host, "127.0.0.1", StringComparison.Ordinal) ||
         string.Equals(host.Host, "localhost", StringComparison.OrdinalIgnoreCase)) &&
        host.Port == 4320;
    var forwardedAuthority = context.Request.Headers.ContainsKey("Forwarded") ||
        context.Request.Headers.ContainsKey("X-Forwarded-Host") ||
        context.Request.Headers.ContainsKey("X-Forwarded-Proto") ||
        context.Request.Headers.ContainsKey("X-Forwarded-For");
    if (!loopbackHost || forwardedAuthority)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new
        {
            contract = "TRN-TECH-077",
            outcome = "UNTRUSTED_LOOPBACK_AUTHORITY",
            syntheticOnly = true,
            memberAuthenticated = false,
            managerAuthenticated = false,
            clinicalReviewCompleted = false,
            nationalReleaseAllowed = false
        });
        return;
    }
    await next();
});

CenterHttp.Store = store;
app.MapGet("/internal/demo/v1/centers/{centerRef}",
    (Func<HttpContext, Task<IResult>>)CenterHttp.ReadAsync);
app.MapPost("/internal/demo/v1/centers/{centerRef}/commands",
    (Func<HttpContext, Task<IResult>>)CenterHttp.WriteAsync);

// Both read models derive from the same fully verified append-only test journal.
// Neither performs a write, approval or actual clinical access.
app.MapGet("/internal/demo/v1/centers/{centerRef}/history",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CenterHistoryHttp.ListAsync);
app.MapGet("/internal/demo/v1/centers/{centerRef}/versions/{version}",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CenterHistoryHttp.AtVersionAsync);
app.MapGet("/internal/demo/v1/centers/{centerRef}/departments/{departmentRef}/roster",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CenterDepartmentRosterHttp.ReadAsync);
app.MapGet("/internal/demo/v1/centers/{centerRef}/doctors/{doctorRef}/affiliation-history",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CenterDoctorAffiliationHttp.ReadAsync);
app.MapGet("/internal/demo/v1/centers/{centerRef}/audit-workspace",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CenterAuditWorkspaceHttp.ReadAsync);

// Separate persistent, TEST-ONLY cost-awareness journal; not a member
// account, legal acknowledgement, insurer estimate or financial backend.
if (Environment.GetEnvironmentVariable("TARANOM_COST_JOURNAL_DEMO") == "CI_EPHEMERAL_ONLY")
{
    Taranom.CenterWorkflow.Demo.CostJournalHttp.Store = new PgSyntheticCostAwarenessJournal(dataSource);
    app.MapPost("/internal/demo/v1/cost-previews",
        (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CostJournalHttp.Register);
    app.MapGet("/internal/demo/v1/cost-previews/{previewId}",
        (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CostJournalHttp.Read);
    app.MapPost("/internal/demo/v1/cost-previews/{previewId}/choices",
        (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CostJournalHttp.Record);
    app.MapGet("/internal/demo/v1/cost-previews/{previewId}/support-link/{caseRef}",
        (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CostSupportHandoffHttp.Inspect);
    app.MapGet("/internal/demo/v1/cost-previews/{previewId}/support-link/{caseRef}/follow-up-preflight",
        (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CostSupportFollowUpHttp.Preflight);
    app.MapGet("/internal/demo/v1/cost-previews/{previewId}/support-link/{caseRef}/timeline",
        (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CostSupportTimelineHttp.ReadPageAsync);
    app.MapGet("/internal/demo/v1/cost-previews/{previewId}/history",
        (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CostHistoryHttp.PageAsync);
    app.MapGet("/internal/demo/v1/cost-previews/{previewId}/versions/{revision}",
        (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.CostHistoryHttp.AtRevisionAsync);
}

// Separate synthetic-only member support case; no actual member/insurer/provider.
Taranom.CenterWorkflow.Demo.SupportHttp.Store =
    new Taranom.CenterWorkflow.Demo.PgSyntheticSupportJournal(supportRuntime);
app.MapGet("/internal/demo/v1/support/{caseRef}",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.SupportHttp.ReadAsync);
app.MapPost("/internal/demo/v1/support/{caseRef}/commands",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.SupportHttp.WriteAsync);
app.MapGet("/internal/demo/v1/support/{caseRef}/history",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.SupportHistoryHttp.ListAsync);
app.MapGet("/internal/demo/v1/support/{caseRef}/versions/{revision}",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.SupportHistoryHttp.AtVersionAsync);

// TRN-TECH-112: a stateless and synthetic-only inspection of the already
// accepted three-market payer/individual-ownership decision. No membership
// activation, payment, wallet, sponsor access or external API.
app.MapPost("/internal/demo/v1/payer-evidence/check",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.PayerEvidenceHttp.CheckAsync);
// Separate request-driven synthetic identity correction + re-verification
// journal. No actual personal data fields or authorization side effects.
app.MapGet("/internal/demo/v1/proofing/{caseRef}",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.ProofingDemoHttp.ReadAsync);
app.MapPost("/internal/demo/v1/proofing/{caseRef}/commands",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.ProofingDemoHttp.WriteAsync);
app.MapGet("/internal/demo/v1/late-evidence/{caseRef}",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.LateEvidenceHttp.ReadAsync);
app.MapPost("/internal/demo/v1/late-evidence/{caseRef}/commands",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.LateEvidenceHttp.WriteAsync);

app.MapFallback("/internal/demo/{**rest}", () => Results.NotFound());
// Exact local member entrypoint is required: the generic root fallback must
// never silently render the CENTER HTML for a MEMBER browser path.
// The center page is an EXACT entrypoint; unknown URLs are not SPA routes.
app.MapGet("/", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
        "index.html"), "text/html; charset=utf-8"));
app.MapGet("/member-demo/", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
        "member-demo", "index.html"), "text/html; charset=utf-8"));
app.MapGet("/member-demo/payer", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
        "member-demo", "index.html"), "text/html; charset=utf-8"));
app.MapGet("/member-demo/proofing", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
        "member-demo", "index.html"), "text/html; charset=utf-8"));
app.MapGet("/member-demo/late-evidence", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
        "member-demo", "index.html"), "text/html; charset=utf-8"));
if (Environment.GetEnvironmentVariable("TARANOM_COST_JOURNAL_DEMO") == "CI_EPHEMERAL_ONLY")
    app.MapGet("/member-demo/costs", () =>
        Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
            "member-demo", "index.html"), "text/html; charset=utf-8"));
app.UseDefaultFiles();
app.UseStaticFiles();
// Unknown paths MUST NOT silently render the center browser page.
// Explicit root default file and explicit member entrypoint remain available.
app.MapFallback(() => Results.NotFound());
app.Run();

// Handler methods deliberately use HttpContext RequestDelegate bindings;
// this avoids generated analyzer fragility for captured route lambdas.
internal static class CenterHttp
{
    internal static PgClinicalCenterJournal Store { get; set; } = null!;

    internal static async Task<IResult> ReadAsync(HttpContext context)
    {
        var centerRef = context.Request.RouteValues["centerRef"]?.ToString();
        if (!ClinicalCenterJournal.IsSyntheticRef(centerRef))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_REFERENCE");
        try
        {
            var snapshot = await Store.ReadAsync(centerRef!);
            return Results.Json(SnapshotDto.From(snapshot, "READ"));
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException
                                  or IOException or OverflowException)
        {
            return Denied(StatusCodes.Status503ServiceUnavailable, "TEST_JOURNAL_UNVERIFIABLE");
        }
    }

    internal static async Task<IResult> WriteAsync(HttpContext context)
    {
        var centerRef = context.Request.RouteValues["centerRef"]?.ToString();
        if (!ClinicalCenterJournal.IsSyntheticRef(centerRef))
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_REFERENCE");
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin is not ("http://127.0.0.1:4320" or "http://localhost:4320"))
            return Denied(StatusCodes.Status403Forbidden, "ORIGIN_DENIED");
        if (context.Request.ContentType is null ||
            !context.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            context.Request.ContentLength is > 768)
            return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");
        try
        {
            var command = await TestCommand.ParseAsync(context.Request);
            if (command is null)
                return Denied(StatusCodes.Status400BadRequest, "INVALID_TEST_COMMAND");
            var domain = new CenterCommand(command.Value.RequestId,
                command.Value.ExpectedVersion, command.Value.Kind, centerRef, centerRef,
                command.Value.DepartmentRef, command.Value.DoctorRef,
                // Only a synthetic test harness may assert these inputs; no actual
                // manager or medical professional is ever authenticated here.
                CenterEvidence.Confirmed, CenterEvidence.Confirmed);
            var change = await Store.ApplyAsync(centerRef!, domain);
            var status = change.Outcome switch
            {
                CenterOutcome.Applied or CenterOutcome.Replayed => StatusCodes.Status200OK,
                CenterOutcome.StaleRevision or CenterOutcome.ConflictingReplay
                    or CenterOutcome.NotApplicable => StatusCodes.Status409Conflict,
                CenterOutcome.ScopeDenied or CenterOutcome.EvidenceDenied
                    or CenterOutcome.EvidenceMissing => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(SnapshotDto.From(change.Snapshot, change.Outcome.ToString()),
                statusCode: status);
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException
                                  or IOException or OverflowException)
        {
            return Denied(StatusCodes.Status503ServiceUnavailable, "TEST_JOURNAL_UNVERIFIABLE");
        }
    }

    private static IResult Denied(int status, string code) =>
        Results.Json(new { contract = "TRN-TECH-073", outcome = code,
            syntheticOnly = true, managerAuthenticated = false,
            professionalQualificationVerified = false, nationalReleaseAllowed = false },
            statusCode: status);
}

internal readonly record struct TestCommand(
    Guid RequestId, long ExpectedVersion, CenterCommandKind Kind,
    string? DepartmentRef, string? DoctorRef)
{
    internal static async Task<TestCommand?> ParseAsync(HttpRequest request)
    {
        var buffer = new byte[769];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await request.Body.ReadAsync(buffer.AsMemory(length));
            if (count == 0) break;
            length += count;
        }
        if (length is < 2 or > 768) return null;
        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 5 ||
                root.EnumerateObject().Select(x => x.Name).Distinct(
                    StringComparer.Ordinal).Count() != 5 ||
                !root.TryGetProperty("requestId", out var id) ||
                !root.TryGetProperty("expectedVersion", out var version) ||
                !root.TryGetProperty("kind", out var kind) ||
                !root.TryGetProperty("departmentRef", out var department) ||
                !root.TryGetProperty("doctorRef", out var doctor) ||
                id.ValueKind != JsonValueKind.String ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt64(out var rev) || rev < 0 ||
                rev == long.MaxValue || kind.ValueKind != JsonValueKind.String ||
                department.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                doctor.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                !Guid.TryParse(id.GetString(), out var uuid) || uuid == Guid.Empty)
                return null;
            var mapped = kind.GetString() switch
            {
                "DEFINE_DEPARTMENT" => CenterCommandKind.DefineDepartment,
                "RECORD_DOCTOR" => CenterCommandKind.RecordDoctorAffiliation,
                "ASSIGN_DOCTOR" => CenterCommandKind.AssignDoctorToDepartment,
                "END_DOCTOR" => CenterCommandKind.EndDoctorAffiliation,
                "UNASSIGN_DOCTOR" => CenterCommandKind.UnassignDoctorFromDepartment,
                _ => (CenterCommandKind)0
            };
            if (mapped == 0) return null;
            var dep = department.ValueKind == JsonValueKind.String ? department.GetString() : null;
            var doc = doctor.ValueKind == JsonValueKind.String ? doctor.GetString() : null;
            if (dep is not null && !ClinicalCenterJournal.IsSyntheticRef(dep) ||
                doc is not null && !ClinicalCenterJournal.IsSyntheticRef(doc))
                return null;
            if (mapped == CenterCommandKind.DefineDepartment && (dep is null || doc is not null) ||
                mapped == CenterCommandKind.RecordDoctorAffiliation && (doc is null || dep is not null) ||
                mapped is (CenterCommandKind.AssignDoctorToDepartment or
                           CenterCommandKind.UnassignDoctorFromDepartment) &&
                    (dep is null || doc is null) ||
                mapped == CenterCommandKind.EndDoctorAffiliation && (doc is null || dep is not null))
                return null;
            return new TestCommand(uuid, rev, mapped, dep, doc);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// The only values exposed are bounded test: references and unverified status.
// An HTTP success never means a professional or manager is authorized.
internal static class SnapshotDto
{
    internal static object From(CenterSnapshot state, string outcome) => new
    {
        contract = "TRN-TECH-073",
        syntheticOnly = true,
        managerAuthenticated = false,
        professionalQualificationVerified = false,
        nationalReleaseAllowed = false,
        centerRef = state.CenterRef,
        currentVersion = state.Version,
        outcome,
        departments = state.Departments.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
        doctors = state.Doctors.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new { doctorRef = x.Key,
                affiliation = x.Value.ToString(),
                departmentRefs = state.DoctorDepartments.TryGetValue(x.Key, out var deps)
                    ? deps.OrderBy(d => d, StringComparer.Ordinal).ToArray()
                    : [] }).ToArray(),
        eventCount = state.Events.Length
    };
}
