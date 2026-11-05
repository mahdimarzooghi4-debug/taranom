using Npgsql;
using Taranom.AppointmentScheduling;
using Taranom.AppointmentScheduling.Postgres;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;
using Taranom.Appointments.Api;

const string IdentityAppointmentScopesPolicy = "taranom.identity.appointment-scopes";

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Taranom");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Taranom is required.");

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<PostgresAppointmentScheduleStore>();
builder.Services.AddSingleton<AppointmentMemberScopeResolver>();
builder.Services.AddSingleton<CenterAccessGrantService>();
builder.Services.AddSingleton<AppointmentDatabaseBootstrap>();
builder.Services.AddSingleton<IPanelAccessEvidenceSource, CentralPanelAccessEvidenceSource>();
builder.Services.AddTaranomOidcPanelApiSecurity(builder.Configuration);
builder.Services.AddAuthorization(options => options.AddPolicy(
    IdentityAppointmentScopesPolicy, policy => policy.RequireAuthenticatedUser()));

var app = builder.Build();
await app.Services.GetRequiredService<AppointmentDatabaseBootstrap>()
    .InitializeAsync(app.Lifetime.ApplicationStopping);
app.UseTaranomPanelAuthorization();

app.MapGet("/api/v1/me/appointment-scopes", async (
    HttpContext context,
    NpgsqlDataSource db,
    CancellationToken cancellationToken) =>
{
    var issuer = context.User.FindFirst("iss")?.Value;
    var subject = context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        return Results.Forbid();

    const string sql = """
        SELECT capability, scope_ref
        FROM taranom_panel_access_grant
        WHERE issuer = @issuer AND subject = @subject
          AND capability IN ('CenterAppointmentsManage', 'MemberAppointmentsManage')
          AND grant_state = 'active'
          AND subject_mapping_current
          AND permission_version_current AND grant_not_revoked AND audit_ready
        ORDER BY capability, scope_ref
        """;
    var scopes = new List<AppointmentAccessScope>();
    await using var connection = await db.OpenConnectionAsync(cancellationToken);
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("issuer", issuer);
    command.Parameters.AddWithValue("subject", subject);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        var capabilityToken = reader.GetString(0);
        var scopeRef = reader.GetString(1);
        var capability = capabilityToken switch
        {
            "CenterAppointmentsManage" => PanelCapability.CenterAppointmentsManage,
            "MemberAppointmentsManage" => PanelCapability.MemberAppointmentsManage,
            _ => (PanelCapability?)null
        };
        if (capability is null) continue;
        var completeEvidence = new PanelAccessFreshness(
            PanelEvidenceState.Confirmed, PanelEvidenceState.Confirmed,
            PanelEvidenceState.Confirmed, PanelEvidenceState.Confirmed,
            PanelEvidenceState.Confirmed);
        var access = PanelAccessPolicy.Evaluate(context.User, issuer, completeEvidence,
            new PanelScopeContext(scopeRef, null, scopeRef), capability.Value);
        if (access.Allowed)
            scopes.Add(new AppointmentAccessScope(capabilityToken, scopeRef));
    }

    context.Response.Headers["Cache-Control"] = "no-store";
    return Results.Ok(new { appointmentScopes = scopes });
}).RequireAuthorization(IdentityAppointmentScopesPolicy);

app.MapGet("/health/ready", async (NpgsqlDataSource db, CancellationToken cancellationToken) =>
{
    await using var connection = await db.OpenConnectionAsync(cancellationToken);
    await using var command = new NpgsqlCommand("SELECT 1", connection);
    await command.ExecuteScalarAsync(cancellationToken);
    return Results.Ok(new { status = "ready" });
}).AllowAnonymous();

var center = app.MapGroup("/api/v1/centers/{centerRef}");
center.MapGet("/access-grants", async (
    string centerRef,
    HttpContext context,
    CenterAccessGrantService grants,
    CancellationToken cancellationToken) =>
{
    var issuer = context.User.FindFirst("iss")?.Value;
    if (string.IsNullOrWhiteSpace(issuer)) return Results.Forbid();
    context.Response.Headers["Cache-Control"] = "no-store";
    var items = await grants.ListAsync(issuer, centerRef, cancellationToken);
    return Results.Ok(new { centerRef, grants = items });
}).RequirePanelCapability(PanelCapability.CenterAccessGrantsManage);

center.MapPost("/access-grants", async (
    string centerRef,
    CenterAccessGrantMutation request,
    HttpContext context,
    CenterAccessGrantService grants,
    CancellationToken cancellationToken) =>
{
    var issuer = context.User.FindFirst("iss")?.Value;
    var subject = context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        return Results.Forbid();
    var outcome = await grants.ApplyAsync(issuer, subject, centerRef, request,
        revoke: false, cancellationToken);
    return CenterGrantResponse(outcome, centerRef, request);
}).RequirePanelCapability(PanelCapability.CenterAccessGrantsManage);

center.MapPost("/access-grants/revoke", async (
    string centerRef,
    CenterAccessGrantMutation request,
    HttpContext context,
    CenterAccessGrantService grants,
    CancellationToken cancellationToken) =>
{
    var issuer = context.User.FindFirst("iss")?.Value;
    var subject = context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        return Results.Forbid();
    var outcome = await grants.ApplyAsync(issuer, subject, centerRef, request,
        revoke: true, cancellationToken);
    return CenterGrantResponse(outcome, centerRef, request);
}).RequirePanelCapability(PanelCapability.CenterAccessGrantsManage);

center.MapGet("/appointment-slots", async (
    string centerRef,
    PostgresAppointmentScheduleStore store,
    CancellationToken cancellationToken) =>
{
    var snapshot = await store.ReadAsync(centerRef, cancellationToken);
    return Results.Ok(new
    {
        centerRef,
        revision = snapshot?.Revision ?? 0,
        slots = snapshot?.Slots.Values.OrderBy(item => item.StartsAt).ToArray()
            ?? Array.Empty<AppointmentSlot>()
    });
}).RequirePanelCapability(PanelCapability.CenterAppointmentsManage);

center.MapGet("/appointments", async (
    string centerRef,
    PostgresAppointmentScheduleStore store,
    CancellationToken cancellationToken) =>
{
    var snapshot = await store.ReadAsync(centerRef, cancellationToken);
    return Results.Ok(new
    {
        centerRef,
        revision = snapshot?.Revision ?? 0,
        appointments = snapshot?.Bookings.Values.OrderBy(item => item.StartsAt).ToArray()
            ?? Array.Empty<AppointmentBooking>()
    });
}).RequirePanelCapability(PanelCapability.CenterAppointmentsManage);

center.MapPost("/appointment-slots", async (
    string centerRef,
    DefineAppointmentSlotRequest request,
    PostgresAppointmentScheduleStore store,
    CancellationToken cancellationToken) =>
{
    var change = await store.ApplyAsync(centerRef,
        snapshot => snapshot.DefineSlot(request.RequestId, request.ExpectedRevision,
            centerRef, request.SlotRef, request.DoctorRef, request.StartsAt, request.EndsAt),
        cancellationToken);
    return AppointmentResponse(change.Outcome, change);
}).RequirePanelCapability(PanelCapability.CenterAppointmentsManage);

var member = app.MapGroup("/api/v1/member");
member.MapGet("/members/{memberRef}/centers/{centerRef}/appointment-slots", async (
    string memberRef,
    string centerRef,
    PostgresAppointmentScheduleStore store,
    CancellationToken cancellationToken) =>
{
    var snapshot = await store.ReadAsync(centerRef, cancellationToken);
    return Results.Ok(new
    {
        centerRef,
        revision = snapshot?.Revision ?? 0,
        slots = snapshot?.Slots.Values.Where(item => item.State == AppointmentSlotState.Open)
            .OrderBy(item => item.StartsAt).ToArray() ?? Array.Empty<AppointmentSlot>()
    });
}).RequirePanelCapability(PanelCapability.MemberAppointmentsManage);

member.MapGet("/members/{memberRef}/centers/{centerRef}/appointments", async (
    string memberRef,
    string centerRef,
    HttpContext context,
    PostgresAppointmentScheduleStore store,
    AppointmentMemberScopeResolver scopes,
    CancellationToken cancellationToken) =>
{
    var resolvedMemberRef = await scopes.ResolveAsync(context.User, memberRef, cancellationToken);
    if (resolvedMemberRef is null) return Results.Forbid();
    var snapshot = await store.ReadAsync(centerRef, cancellationToken);
    var appointments = snapshot?.Bookings.Values
        .Where(item => string.Equals(item.MemberRef, resolvedMemberRef, StringComparison.Ordinal))
        .OrderBy(item => item.StartsAt).ToArray() ?? Array.Empty<AppointmentBooking>();
    return Results.Ok(new { centerRef, appointments });
}).RequirePanelCapability(PanelCapability.MemberAppointmentsManage);

member.MapPost("/members/{memberRef}/centers/{centerRef}/appointments", async (
    string memberRef,
    string centerRef,
    BookAppointmentRequest request,
    HttpContext context,
    PostgresAppointmentScheduleStore store,
    AppointmentMemberScopeResolver scopes,
    CancellationToken cancellationToken) =>
{
    var resolvedMemberRef = await scopes.ResolveAsync(context.User, memberRef, cancellationToken);
    if (resolvedMemberRef is null) return Results.Forbid();
    var change = await store.ApplyAsync(centerRef,
        snapshot => snapshot.Book(request.RequestId, request.ExpectedRevision,
            resolvedMemberRef, request.SlotRef), cancellationToken);
    return AppointmentResponse(change.Outcome, change);
}).RequirePanelCapability(PanelCapability.MemberAppointmentsManage);

member.MapPost("/members/{memberRef}/centers/{centerRef}/appointments/{bookingRef:guid}/cancel", async (
    string memberRef,
    string centerRef,
    Guid bookingRef,
    CancelAppointmentRequest request,
    HttpContext context,
    PostgresAppointmentScheduleStore store,
    AppointmentMemberScopeResolver scopes,
    CancellationToken cancellationToken) =>
{
    var resolvedMemberRef = await scopes.ResolveAsync(context.User, memberRef, cancellationToken);
    if (resolvedMemberRef is null) return Results.Forbid();
    var current = await store.ReadAsync(centerRef, cancellationToken);
    if (current is null || !current.Bookings.TryGetValue(bookingRef, out var booking) ||
        !string.Equals(booking.MemberRef, resolvedMemberRef, StringComparison.Ordinal))
        return Results.NotFound();
    var change = await store.ApplyAsync(centerRef, snapshot =>
    {
        if (!snapshot.Bookings.TryGetValue(bookingRef, out var currentBooking) ||
            currentBooking.State != AppointmentBookingState.Booked)
            return new AppointmentScheduleChange(
                AppointmentCommandOutcome.NotFound, snapshot, null, null);
        return snapshot.Cancel(request.RequestId, request.ExpectedRevision,
            resolvedMemberRef, currentBooking.SlotRef);
    }, cancellationToken);
    return AppointmentResponse(change.Outcome, change);
}).RequirePanelCapability(PanelCapability.MemberAppointmentsManage);

app.Run();

static IResult AppointmentResponse(
    AppointmentCommandOutcome outcome,
    AppointmentPersistenceChange change) => outcome switch
{
    AppointmentCommandOutcome.Applied or AppointmentCommandOutcome.Replayed =>
        Results.Ok(new { outcome = outcome.ToString(), revision = change.Snapshot.Revision,
            slot = change.Slot, appointment = change.Booking }),
    AppointmentCommandOutcome.StaleRevision or AppointmentCommandOutcome.SlotUnavailable =>
        Results.Conflict(new { code = outcome.ToString(), revision = change.Snapshot.Revision }),
    AppointmentCommandOutcome.NotFound => Results.NotFound(),
    AppointmentCommandOutcome.NotOwner or AppointmentCommandOutcome.ScopeDenied => Results.Forbid(),
    _ => Results.BadRequest(new { code = outcome.ToString() })
};

static IResult CenterGrantResponse(
    CenterAccessGrantOutcome outcome,
    string centerRef,
    CenterAccessGrantMutation request) => outcome switch
{
    CenterAccessGrantOutcome.Applied or CenterAccessGrantOutcome.Replayed =>
        Results.Ok(new
        {
            outcome = outcome.ToString(),
            centerRef,
            targetSubject = request.TargetSubject,
            capability = request.Capability,
            authorizationRefreshRequired = false
        }),
    CenterAccessGrantOutcome.NotFound => Results.NotFound(),
    CenterAccessGrantOutcome.Conflict => Results.Conflict(new { code = "IDEMPOTENCY_KEY_CONFLICT" }),
    _ => Results.BadRequest(new { code = "INVALID_CENTER_ACCESS_GRANT" })
};

public sealed record DefineAppointmentSlotRequest(
    Guid RequestId, long ExpectedRevision, string SlotRef, string DoctorRef,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record BookAppointmentRequest(Guid RequestId, long ExpectedRevision, string SlotRef);
public sealed record CancelAppointmentRequest(Guid RequestId, long ExpectedRevision);
public sealed record AppointmentAccessScope(string Capability, string ScopeRef);

public sealed class AppointmentDatabaseBootstrap(
    NpgsqlDataSource dataSource,
    PostgresAppointmentScheduleStore appointments)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await appointments.InitializeAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS taranom_panel_access_grant (
                issuer text NOT NULL,
                subject text NOT NULL,
                capability text NOT NULL,
                scope_ref text NOT NULL,
                grant_state text NOT NULL CHECK (grant_state IN ('active', 'revoked')),
                session_current boolean NOT NULL DEFAULT false,
                subject_mapping_current boolean NOT NULL DEFAULT false,
                permission_version_current boolean NOT NULL DEFAULT false,
                grant_not_revoked boolean NOT NULL DEFAULT false,
                audit_ready boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (issuer, subject, capability, scope_ref)
            );
            CREATE INDEX IF NOT EXISTS ix_taranom_panel_access_grant_lookup
                ON taranom_panel_access_grant (issuer, subject, capability, grant_state);
            CREATE TABLE IF NOT EXISTS taranom_panel_access_grant_audit (
                event_id uuid PRIMARY KEY,
                request_id uuid NOT NULL,
                actor_issuer text NOT NULL,
                actor_subject text NOT NULL,
                target_issuer text NOT NULL,
                target_subject text NOT NULL,
                capability text NOT NULL,
                scope_ref text NOT NULL,
                action text NOT NULL CHECK (action IN ('granted', 'revoked')),
                reason text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (actor_issuer, actor_subject, request_id)
            );
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public partial class Program { }
