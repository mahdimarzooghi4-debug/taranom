using System.Text.Json;
using Npgsql;
using Taranom.ClinicalCenterGovernance;
using Taranom.ClinicalCenterJournal.Postgres;

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
await Taranom.CenterWorkflow.Demo.PgSyntheticSupportJournal.InitializeAsync(dataSource);
var store = new PgClinicalCenterJournal(dataSource);

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

// Separate synthetic-only member support case; no actual member/insurer/provider.
Taranom.CenterWorkflow.Demo.SupportHttp.Store =
    new Taranom.CenterWorkflow.Demo.PgSyntheticSupportJournal(dataSource);
app.MapGet("/internal/demo/v1/support/{caseRef}",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.SupportHttp.ReadAsync);
app.MapPost("/internal/demo/v1/support/{caseRef}/commands",
    (Func<HttpContext, Task<IResult>>)Taranom.CenterWorkflow.Demo.SupportHttp.WriteAsync);

app.MapFallback("/internal/demo/{**rest}", () => Results.NotFound());
// Exact local member entrypoint is required: the generic root fallback must
// never silently render the CENTER HTML for a MEMBER browser path.
app.MapGet("/member-demo/", () =>
    Results.File(Path.Combine(app.Environment.WebRootPath ?? "wwwroot",
        "member-demo", "index.html"), "text/html; charset=utf-8"));
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
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
                mapped == CenterCommandKind.AssignDoctorToDepartment && (dep is null || doc is null) ||
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
