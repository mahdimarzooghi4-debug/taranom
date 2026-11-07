using System.Security.Claims;
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
          AND (capability = 'MemberAppointmentsManage' OR EXISTS (
              SELECT 1 FROM taranom_canonical_center canonical
              WHERE canonical.center_ref=scope_ref))
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

app.MapGet("/api/v1/me/appointments", async (
    HttpContext context,
    NpgsqlDataSource db,
    CancellationToken cancellationToken) =>
{
    var issuer = context.User.FindFirst("iss")?.Value;
    var subject = context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        return Results.Forbid();

    const string sql = """
        SELECT schedule.center_ref, schedule.revision,
               booking.value ->> 'bookingRef',
               booking.value ->> 'memberRef',
               booking.value ->> 'slotRef',
               (booking.value ->> 'startsAt')::timestamptz,
               CASE booking.value ->> 'state'
                   WHEN '1' THEN 'Booked'
                   WHEN '2' THEN 'CancelledByMember'
                   ELSE booking.value ->> 'state'
               END
        FROM taranom_appointment_schedule AS schedule
        CROSS JOIN LATERAL jsonb_array_elements(
            CASE jsonb_typeof(schedule.snapshot -> 'bookings')
                WHEN 'array' THEN schedule.snapshot -> 'bookings'
                WHEN 'object' THEN COALESCE((
                    SELECT jsonb_agg(item.value)
                    FROM jsonb_each(schedule.snapshot -> 'bookings') AS item(key, value)
                ), '[]'::jsonb)
                ELSE '[]'::jsonb
            END
        ) AS booking(value)
        WHERE EXISTS (
            SELECT 1
            FROM taranom_panel_access_grant AS grant_row
            WHERE grant_row.issuer = @issuer
              AND grant_row.subject = @subject
              AND grant_row.capability = 'MemberAppointmentsManage'
              AND grant_row.scope_ref = booking.value ->> 'memberRef'
              AND grant_row.grant_state = 'active'
              AND grant_row.subject_mapping_current
              AND grant_row.permission_version_current
              AND grant_row.grant_not_revoked
              AND grant_row.audit_ready
        )
        ORDER BY 6, 1, 3
        """;
    var appointments = new List<MemberAppointmentView>();
    await using var connection = await db.OpenConnectionAsync(cancellationToken);
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("issuer", issuer);
    command.Parameters.AddWithValue("subject", subject);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        if (!Guid.TryParse(reader.GetString(2), out var bookingRef))
            continue;
        appointments.Add(new MemberAppointmentView(
            reader.GetString(0),
            reader.GetInt64(1),
            bookingRef,
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetString(6)));
    }

    context.Response.Headers["Cache-Control"] = "no-store";
    return Results.Ok(new { appointments });
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
    HttpContext context,
    PostgresAppointmentScheduleStore store,
    CancellationToken cancellationToken) =>
{
    var actor = ResolveAppointmentAuditActor(context.User);
    if (actor is null) return Results.Forbid();
    var change = await store.ApplyAsync(centerRef, actor,
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
    NpgsqlDataSource db,
    CancellationToken cancellationToken) =>
{
    if (!await CanonicalCenterGuard.ExistsAsync(db, centerRef, cancellationToken))
        return Results.NotFound(new {code="CENTER_NOT_ADMIN_DEFINED"});
    // Member-facing capacity is a form of clinical-service publication.
    // No authority for publication is configured; never expose slots
    // solely because someone knows an opaque center reference.
    return Results.Json(new { code = "CENTER_PUBLICATION_NOT_AUTHORIZED" },
        statusCode: StatusCodes.Status403Forbidden);
    /* Future implementation requires a separate verified publication gate.
    var snapshot = await store.ReadAsync(centerRef, cancellationToken);
    return Results.Ok(new
    {
        centerRef,
        revision = snapshot?.Revision ?? 0,
        slots = snapshot?.Slots.Values.Where(item => item.State == AppointmentSlotState.Open)
            .OrderBy(item => item.StartsAt).ToArray() ?? Array.Empty<AppointmentSlot>()
    }); */
}).RequirePanelCapability(PanelCapability.MemberAppointmentsManage);

member.MapGet("/members/{memberRef}/centers/{centerRef}/appointments", async (
    string memberRef,
    string centerRef,
    HttpContext context,
    PostgresAppointmentScheduleStore store,
    AppointmentMemberScopeResolver scopes,
    NpgsqlDataSource db,
    CancellationToken cancellationToken) =>
{
    var resolvedMemberRef = await scopes.ResolveAsync(context.User, memberRef, cancellationToken);
    if (resolvedMemberRef is null) return Results.Forbid();
    if (!await CanonicalCenterGuard.ExistsAsync(db, centerRef, cancellationToken))
        return Results.NotFound(new {code="CENTER_NOT_ADMIN_DEFINED"});
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
    NpgsqlDataSource db,
    CancellationToken cancellationToken) =>
{
    var resolvedMemberRef = await scopes.ResolveAsync(context.User, memberRef, cancellationToken);
    var actor = ResolveAppointmentAuditActor(context.User);
    if (resolvedMemberRef is null || actor is null) return Results.Forbid();
    if (!await CanonicalCenterGuard.ExistsAsync(db, centerRef, cancellationToken))
        return Results.NotFound(new {code="CENTER_NOT_ADMIN_DEFINED"});
    // Unknown or Admin-defined-only centers are not approved providers.
    // Booking is blocked independently of the center search UI, capacity,
    // member scopes, and knowledge of exact center and slot references.
    return Results.Json(new { code = "CENTER_PUBLICATION_NOT_AUTHORIZED" },
        statusCode: StatusCodes.Status403Forbidden);
}).RequirePanelCapability(PanelCapability.MemberAppointmentsManage);

member.MapPost("/members/{memberRef}/centers/{centerRef}/appointments/{bookingRef:guid}/cancel", async (
    string memberRef,
    string centerRef,
    Guid bookingRef,
    CancelAppointmentRequest request,
    HttpContext context,
    PostgresAppointmentScheduleStore store,
    AppointmentMemberScopeResolver scopes,
    NpgsqlDataSource db,
    CancellationToken cancellationToken) =>
{
    var resolvedMemberRef = await scopes.ResolveAsync(context.User, memberRef, cancellationToken);
    var actor = ResolveAppointmentAuditActor(context.User);
    if (resolvedMemberRef is null || actor is null) return Results.Forbid();
    if (!await CanonicalCenterGuard.ExistsAsync(db, centerRef, cancellationToken))
        return Results.NotFound(new {code="CENTER_NOT_ADMIN_DEFINED"});
    var current = await store.ReadAsync(centerRef, cancellationToken);
    if (current is null || !current.Bookings.TryGetValue(bookingRef, out var booking) ||
        !string.Equals(booking.MemberRef, resolvedMemberRef, StringComparison.Ordinal))
        return Results.NotFound();
    var change = await store.ApplyAsync(centerRef, actor, snapshot =>
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


app.MapGet("/api/v1/me/available-appointment-centers", async (
    HttpContext context,
    NpgsqlDataSource db,
    CancellationToken cancellationToken) =>
{
    var issuer = context.User.FindFirst("iss")?.Value;
    var subject = context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        return Results.Forbid();

    // An Admin-created record or an open appointment slot is NOT an approved
    // clinical-service publication. No publication authority/evidence exists
    // yet, so never promote live capacity into patient-facing discovery.
    const string sql = """
        SELECT EXISTS (
            SELECT 1 FROM taranom_panel_access_grant
            WHERE issuer=@issuer AND subject=@subject
              AND capability='MemberAppointmentsManage' AND grant_state='active'
              AND session_current AND subject_mapping_current
              AND permission_version_current AND grant_not_revoked AND audit_ready
        )
        """;
    await using var connection = await db.OpenConnectionAsync(cancellationToken);
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("issuer", issuer);
    command.Parameters.AddWithValue("subject", subject);
    if (await command.ExecuteScalarAsync(cancellationToken) is not true)
        return Results.Forbid();
    context.Response.Headers["Cache-Control"] = "no-store";
    return Results.Ok(new { centers = Array.Empty<object>(),
        publicationGateRequired = true });
}).RequireAuthorization(IdentityAppointmentScopesPolicy);

app.MapCenterDirectoryDraftEndpoints(IdentityAppointmentScopesPolicy);
app.MapMemberProfileEndpoints(IdentityAppointmentScopesPolicy);
app.MapMemberGroupDraftEndpoints(IdentityAppointmentScopesPolicy);
app.MapMemberGroupInvitationEndpoints(IdentityAppointmentScopesPolicy);
app.MapMemberSupportEndpoints(IdentityAppointmentScopesPolicy);
app.MapSupportOperationsEndpoints();
app.MapMemberNotificationEndpoints(IdentityAppointmentScopesPolicy);
app.MapAdminCenterRegistryEndpoints();
app.MapCenterManagerRequestEndpoints(IdentityAppointmentScopesPolicy);
app.MapCenterManagerCaseWorkspaceEndpoints();

app.Run();

static AppointmentAuditActor? ResolveAppointmentAuditActor(ClaimsPrincipal principal)
{
    var identity = principal.Identities.FirstOrDefault(item =>
        item.IsAuthenticated &&
        !string.IsNullOrWhiteSpace(item.FindFirst("iss")?.Value) &&
        !string.IsNullOrWhiteSpace(item.FindFirst("sub")?.Value));
    if (identity is null) return null;

    var issuer = identity.FindFirst("iss")?.Value;
    var subject = identity.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ||
        issuer.Length > 512 || subject.Length > 256 ||
        issuer.Any(char.IsControl) || subject.Any(char.IsControl))
        return null;

    return new AppointmentAuditActor(issuer, subject);
}

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
    CenterAccessGrantOutcome.Forbidden => Results.Forbid(),
    CenterAccessGrantOutcome.Conflict => Results.Conflict(new { code = "IDEMPOTENCY_KEY_CONFLICT" }),
    _ => Results.BadRequest(new { code = "INVALID_CENTER_ACCESS_GRANT" })
};

public sealed record DefineAppointmentSlotRequest(
    Guid RequestId, long ExpectedRevision, string SlotRef, string DoctorRef,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record BookAppointmentRequest(Guid RequestId, long ExpectedRevision, string SlotRef);
public sealed record CancelAppointmentRequest(Guid RequestId, long ExpectedRevision);
public sealed record AppointmentAccessScope(string Capability, string ScopeRef);
public sealed record MemberAppointmentView(
    string CenterRef,
    long ScheduleRevision,
    Guid BookingRef,
    string MemberRef,
    string SlotRef,
    DateTimeOffset StartsAt,
    string State);

public sealed class AppointmentDatabaseBootstrap(
    NpgsqlDataSource dataSource,
    PostgresAppointmentScheduleStore appointments)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await appointments.InitializeAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var schemaLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended('taranom.panel-api.schema.v1', 0))", connection, transaction))
            await schemaLock.ExecuteNonQueryAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS taranom_member_support_request (
                sequence_no bigserial PRIMARY KEY,
                issuer text NOT NULL,
                subject text NOT NULL,
                request_id uuid NOT NULL,
                request_ref uuid NOT NULL UNIQUE,
                topic text NOT NULL CHECK (topic IN ('account','group','appointments','privacy')),
                message text NOT NULL,
                fingerprint text NOT NULL,
                state text NOT NULL CHECK (state = 'Registered'),
                created_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (issuer,subject,request_id)
            );
            ALTER TABLE taranom_member_support_request ADD COLUMN IF NOT EXISTS revision bigint NOT NULL DEFAULT 0;
            ALTER TABLE taranom_member_support_request ADD COLUMN IF NOT EXISTS reply text NULL;
            ALTER TABLE taranom_member_support_request ADD COLUMN IF NOT EXISTS replied_at timestamptz NULL;
            ALTER TABLE taranom_member_support_request DROP CONSTRAINT IF EXISTS taranom_member_support_request_state_check;
            ALTER TABLE taranom_member_support_request ADD CONSTRAINT taranom_member_support_request_state_check
                CHECK ((state='Registered' AND revision=0 AND reply IS NULL AND replied_at IS NULL)
                    OR (state='Answered' AND revision=1 AND reply IS NOT NULL AND replied_at IS NOT NULL));
            CREATE TABLE IF NOT EXISTS taranom_member_support_notification (
                sequence_no bigserial PRIMARY KEY,
                request_ref uuid NOT NULL UNIQUE REFERENCES taranom_member_support_request(request_ref),
                created_at timestamptz NOT NULL,
                read_at timestamptz NULL
            );
            INSERT INTO taranom_member_support_notification(request_ref,created_at)
                SELECT s.request_ref,s.replied_at FROM taranom_member_support_request s
                WHERE s.state='Answered' AND NOT EXISTS (
                    SELECT 1 FROM taranom_member_support_notification n WHERE n.request_ref=s.request_ref)
                ORDER BY s.replied_at,s.request_ref
                ON CONFLICT (request_ref) DO NOTHING;
            CREATE TABLE IF NOT EXISTS taranom_support_reply_receipt (
                issuer text NOT NULL,
                subject text NOT NULL,
                request_id uuid NOT NULL,
                request_ref uuid NOT NULL REFERENCES taranom_member_support_request(request_ref),
                fingerprint text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (issuer,subject,request_id)
            );
            CREATE INDEX IF NOT EXISTS ix_taranom_member_support_owner
                ON taranom_member_support_request (issuer,subject,sequence_no DESC);
            CREATE TABLE IF NOT EXISTS taranom_member_group_invite_draft (
                sequence_no bigserial PRIMARY KEY,
                issuer text NOT NULL,
                subject text NOT NULL,
                draft_ref uuid NOT NULL UNIQUE,
                request_id uuid NOT NULL,
                method text NOT NULL CHECK (method IN ('NewRegistration','ExistingMember')),
                recipient_label text NOT NULL,
                fingerprint text NOT NULL,
                state text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                withdrawn_at timestamptz NULL,
                CHECK ((state='Draft' AND withdrawn_at IS NULL) OR (state='Withdrawn' AND withdrawn_at IS NOT NULL)),
                UNIQUE (issuer,subject,request_id)
            );
            CREATE INDEX IF NOT EXISTS ix_taranom_group_draft_owner
                ON taranom_member_group_invite_draft(issuer,subject,sequence_no DESC);
            CREATE TABLE IF NOT EXISTS taranom_member_group_invitation (
                sequence_no bigserial PRIMARY KEY,
                invitation_ref uuid NOT NULL UNIQUE,
                draft_ref uuid NOT NULL REFERENCES taranom_member_group_invite_draft(draft_ref),
                request_id uuid NOT NULL,
                token_hash text NOT NULL UNIQUE,
                state text NOT NULL CHECK (state IN ('Pending','Accepted','Declined','Revoked')),
                created_at timestamptz NOT NULL DEFAULT now(),
                expires_at timestamptz NOT NULL,
                responded_at timestamptz NULL,
                recipient_issuer text NULL,
                recipient_subject text NULL,
                consent_version text NULL,
                UNIQUE(draft_ref,request_id),
                CHECK (expires_at>created_at),
                CHECK ((state IN ('Pending','Revoked') AND responded_at IS NULL AND recipient_issuer IS NULL AND recipient_subject IS NULL AND consent_version IS NULL)
                    OR (state IN ('Accepted','Declined') AND responded_at IS NOT NULL AND recipient_issuer IS NOT NULL AND recipient_subject IS NOT NULL AND consent_version='group-invitation-v1'))
            );
            CREATE INDEX IF NOT EXISTS ix_taranom_group_invitation_draft
                ON taranom_member_group_invitation(draft_ref,sequence_no DESC);
            CREATE INDEX IF NOT EXISTS ix_taranom_group_invitation_recipient
                ON taranom_member_group_invitation(recipient_issuer,recipient_subject,sequence_no DESC);
            CREATE TABLE IF NOT EXISTS taranom_member_profile (
                issuer text NOT NULL,
                subject text NOT NULL,
                revision bigint NOT NULL CHECK (revision > 0),
                full_name text NOT NULL,
                birth_date date NULL,
                city text NOT NULL,
                PRIMARY KEY (issuer,subject)
            );
            CREATE TABLE IF NOT EXISTS taranom_member_profile_audit (
                issuer text NOT NULL,
                subject text NOT NULL,
                request_id uuid NOT NULL,
                fingerprint text NOT NULL,
                result_revision bigint NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (issuer,subject,request_id)
            );
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

            CREATE TABLE IF NOT EXISTS taranom_canonical_center (
                center_ref text PRIMARY KEY,
                display_name text NOT NULL,
                city text NOT NULL,
                defined_by_issuer text NOT NULL,
                defined_by_subject text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS taranom_canonical_center_creation_receipt (
                issuer text NOT NULL,
                subject text NOT NULL,
                request_id uuid NOT NULL,
                fingerprint char(64) NOT NULL,
                center_ref text NOT NULL REFERENCES taranom_canonical_center(center_ref),
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (issuer,subject,request_id)
            );
            CREATE TABLE IF NOT EXISTS taranom_center_directory_draft (
                center_ref text PRIMARY KEY,
                revision bigint NOT NULL CHECK (revision > 0),
                display_name text NOT NULL,
                city text NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS taranom_center_doctor_draft (
                center_ref text NOT NULL,
                doctor_ref text NOT NULL,
                revision bigint NOT NULL CHECK (revision > 0),
                display_name text NOT NULL,
                specialty text NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (center_ref, doctor_ref)
            );
            CREATE TABLE IF NOT EXISTS taranom_center_directory_draft_audit (
                center_ref text NOT NULL,
                actor_issuer text NOT NULL,
                actor_subject text NOT NULL,
                request_id uuid NOT NULL,
                action text NOT NULL CHECK (action IN ('center-profile','doctor-claim')),
                target_ref text NOT NULL,
                fingerprint char(64) NOT NULL,
                result_revision bigint NOT NULL CHECK (result_revision > 0),
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (center_ref,actor_issuer,actor_subject,request_id)
            );
            CREATE TABLE IF NOT EXISTS taranom_center_manager_request (
                request_id uuid PRIMARY KEY,
                issuer text NOT NULL,
                subject text NOT NULL,
                center_ref text NOT NULL REFERENCES taranom_canonical_center(center_ref),
                reason text NOT NULL CHECK (char_length(reason) BETWEEN 10 AND 500),
                fingerprint char(64) NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (issuer, subject, center_ref)
            );
            CREATE TABLE IF NOT EXISTS taranom_center_manager_request_review (
                request_id uuid PRIMARY KEY REFERENCES taranom_center_manager_request(request_id),
                reviewer_issuer text NOT NULL,
                reviewer_subject text NOT NULL,
                command_id uuid NOT NULL,
                decision text NOT NULL CHECK (decision IN
                    ('REVIEWED_PENDING_VERIFICATION','DECLINED')),
                reason text NOT NULL CHECK (char_length(reason) BETWEEN 10 AND 500),
                reviewed_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (reviewer_issuer,reviewer_subject,command_id)
            );
            CREATE INDEX IF NOT EXISTS ix_taranom_manager_requests_owner
                ON taranom_center_manager_request(issuer,subject,request_id);
                        CREATE OR REPLACE FUNCTION taranom_reject_center_directory_audit_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS '
            BEGIN
                RAISE EXCEPTION ''CENTER_DIRECTORY_AUDIT_APPEND_ONLY''
                    USING ERRCODE = ''55000'';
                RETURN NULL;
            END;
            ';
            DROP TRIGGER IF EXISTS trg_taranom_manager_request_history_write
                ON taranom_center_manager_request;
            CREATE TRIGGER trg_taranom_manager_request_history_write
                BEFORE UPDATE OR DELETE ON taranom_center_manager_request
                FOR EACH ROW EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            DROP TRIGGER IF EXISTS trg_taranom_manager_request_history_truncate
                ON taranom_center_manager_request;
            CREATE TRIGGER trg_taranom_manager_request_history_truncate
                BEFORE TRUNCATE ON taranom_center_manager_request
                FOR EACH STATEMENT EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            DROP TRIGGER IF EXISTS trg_taranom_manager_review_history_write
                ON taranom_center_manager_request_review;
            CREATE TRIGGER trg_taranom_manager_review_history_write
                BEFORE UPDATE OR DELETE ON taranom_center_manager_request_review
                FOR EACH ROW EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            DROP TRIGGER IF EXISTS trg_taranom_manager_review_history_truncate
                ON taranom_center_manager_request_review;
            CREATE TRIGGER trg_taranom_manager_review_history_truncate
                BEFORE TRUNCATE ON taranom_center_manager_request_review
                FOR EACH STATEMENT EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
                        DROP TRIGGER IF EXISTS trg_taranom_canonical_center_update_delete
                ON taranom_canonical_center;
            CREATE TRIGGER trg_taranom_canonical_center_update_delete
                BEFORE UPDATE OR DELETE ON taranom_canonical_center
                FOR EACH ROW EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            DROP TRIGGER IF EXISTS trg_taranom_canonical_center_truncate
                ON taranom_canonical_center;
            CREATE TRIGGER trg_taranom_canonical_center_truncate
                BEFORE TRUNCATE ON taranom_canonical_center
                FOR EACH STATEMENT EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            DROP TRIGGER IF EXISTS trg_taranom_canonical_center_receipt_mutation
                ON taranom_canonical_center_creation_receipt;
            CREATE TRIGGER trg_taranom_canonical_center_receipt_mutation
                BEFORE UPDATE OR DELETE ON taranom_canonical_center_creation_receipt
                FOR EACH ROW EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            DROP TRIGGER IF EXISTS trg_taranom_canonical_center_receipt_truncate
                ON taranom_canonical_center_creation_receipt;
            CREATE TRIGGER trg_taranom_canonical_center_receipt_truncate
                BEFORE TRUNCATE ON taranom_canonical_center_creation_receipt
                FOR EACH STATEMENT EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            DROP TRIGGER IF EXISTS trg_taranom_center_directory_audit_update_delete
                ON taranom_center_directory_draft_audit;
            CREATE TRIGGER trg_taranom_center_directory_audit_update_delete
                BEFORE UPDATE OR DELETE ON taranom_center_directory_draft_audit
                FOR EACH ROW EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            DROP TRIGGER IF EXISTS trg_taranom_center_directory_audit_truncate
                ON taranom_center_directory_draft_audit;
            CREATE TRIGGER trg_taranom_center_directory_audit_truncate
                BEFORE TRUNCATE ON taranom_center_directory_draft_audit
                FOR EACH STATEMENT EXECUTE FUNCTION taranom_reject_center_directory_audit_mutation();
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public partial class Program { }
