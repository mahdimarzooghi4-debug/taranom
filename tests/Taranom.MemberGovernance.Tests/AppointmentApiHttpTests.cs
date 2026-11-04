using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class AppointmentApiHttpTests
{
    [Fact]
    public async Task CenterManagerCanGrantAndRevokeOnlyCenterStaffCapabilities()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(cancellationToken);
        var centerRef = "center-admin-" + Guid.NewGuid().ToString("N");
        var otherCenterRef = "center-admin-other-" + Guid.NewGuid().ToString("N");
        var managerSubject = "center-admin-subject-" + Guid.NewGuid().ToString("N");
        var staffSubject = "center-staff-subject-" + Guid.NewGuid().ToString("N");
        await setup.GrantAsync(managerSubject, "CenterAccessGrantsManage", centerRef, cancellationToken);

        using var manager = setup.AuthenticatedClient(
            managerSubject, centerRef, "CenterAccessGrantsManage");
        var accessGrants = $"/api/v1/centers/{centerRef}/access-grants";
        using var appointmentOnly = setup.AuthenticatedClient(
            staffSubject, centerRef, "CenterAppointmentsManage");
        var managerOnlyRoute = await appointmentOnly.GetAsync(accessGrants, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, managerOnlyRoute.StatusCode);

        var requestId = Guid.NewGuid();
        var grantPayload = new
        {
            requestId,
            targetSubject = staffSubject,
            capability = "CenterAppointmentsManage",
            reason = "Assigned center appointment desk duties"
        };
        var granted = await manager.PostAsJsonAsync(accessGrants, grantPayload, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        using var grantJson = await granted.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        Assert.Equal("Applied", grantJson!.RootElement.GetProperty("outcome").GetString());
        Assert.True(grantJson.RootElement.GetProperty("authorizationRefreshRequired").GetBoolean());

        var listed = await manager.GetAsync(accessGrants, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var listJson = await listed.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        Assert.Contains(listJson!.RootElement.GetProperty("grants").EnumerateArray(),
            item => item.GetProperty("targetSubject").GetString() == staffSubject &&
                item.GetProperty("capability").GetString() == "CenterAppointmentsManage");

        var replayed = await manager.PostAsJsonAsync(accessGrants, grantPayload, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        using var replayJson = await replayed.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        Assert.Equal("Replayed", replayJson!.RootElement.GetProperty("outcome").GetString());

        var conflictingReplay = await manager.PostAsJsonAsync(accessGrants,
            new { requestId, targetSubject = "different-staff", capability = "CenterAppointmentsManage",
                reason = grantPayload.reason }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, conflictingReplay.StatusCode);

        using var staff = setup.AuthenticatedClient(
            staffSubject, centerRef, "CenterAppointmentsManage");
        var authorized = await staff.GetAsync(
            $"/api/v1/centers/{centerRef}/appointments", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        var crossCenter = await manager.PostAsJsonAsync(
            $"/api/v1/centers/{otherCenterRef}/access-grants", grantPayload, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, crossCenter.StatusCode);

        var memberCapability = await manager.PostAsJsonAsync(accessGrants,
            new { requestId = Guid.NewGuid(), targetSubject = staffSubject,
                capability = "MemberAppointmentsManage", reason = "Not a center capability" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, memberCapability.StatusCode);

        var revoked = await manager.PostAsJsonAsync(accessGrants + "/revoke",
            new { requestId = Guid.NewGuid(), targetSubject = staffSubject,
                capability = "CenterAppointmentsManage", reason = "Staff access ended" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var deniedAfterRevocation = await staff.GetAsync(
            $"/api/v1/centers/{centerRef}/appointments", cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedAfterRevocation.StatusCode);
        Assert.Equal(2, await setup.CountGrantAuditEventsAsync(centerRef, cancellationToken));
    }

    [Fact]
    public async Task AuthenticatedIdentitySeesOnlyItsCurrentAppointmentScopes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(cancellationToken);
        using var anonymousClient = setup.Factory.CreateClient();
        var anonymous = await anonymousClient.GetAsync(
            "/api/v1/me/appointment-scopes", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var subject = "scope-subject-" + Guid.NewGuid().ToString("N");
        var otherSubject = "scope-other-" + Guid.NewGuid().ToString("N");
        await setup.GrantAsync(subject, "CenterAppointmentsManage", "center-scope-a", cancellationToken);
        await setup.GrantAsync(subject, "MemberAppointmentsManage", "member-scope-a", cancellationToken);
        await setup.GrantAsync(subject, "CenterAppointmentsManage", "center-scope-stale", cancellationToken,
            currentEvidence: false);
        await setup.GrantAsync(subject, "CenterAppointmentsManage", "center-scope-revoked", cancellationToken,
            grantState: "revoked");
        await setup.GrantAsync(otherSubject, "CenterAppointmentsManage", "center-scope-other", cancellationToken);

        using var withoutGrantClaims = setup.AuthenticatedIdentityClient(subject);
        var noClaimResponse = await withoutGrantClaims.GetAsync(
            "/api/v1/me/appointment-scopes", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, noClaimResponse.StatusCode);
        using var noClaimPayload = await noClaimResponse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        Assert.Empty(noClaimPayload!.RootElement.GetProperty("appointmentScopes").EnumerateArray());

        using var client = setup.AuthenticatedIdentityClient(subject,
            ("center-scope-a", "CenterAppointmentsManage"),
            ("member-scope-a", "MemberAppointmentsManage"));
        var response = await client.GetAsync("/api/v1/me/appointment-scopes", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var scopes = payload!.RootElement.GetProperty("appointmentScopes").EnumerateArray()
            .Select(item => $"{item.GetProperty("capability").GetString()}|{item.GetProperty("scopeRef").GetString()}")
            .ToArray();
        Assert.Equal(new[]
        {
            "CenterAppointmentsManage|center-scope-a",
            "MemberAppointmentsManage|member-scope-a"
        }, scopes);
    }

    [Fact]
    public async Task AppointmentRoutesRequireAuthenticationAndExactCenterCapability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(cancellationToken);
        using var client = setup.Factory.CreateClient();
        var centerRef = "center-http-" + Guid.NewGuid().ToString("N");
        var subject = "center-subject-" + Guid.NewGuid().ToString("N");
        var staleSubject = "center-stale-subject-" + Guid.NewGuid().ToString("N");

        var anonymous = await client.GetAsync(
            $"/api/v1/centers/{centerRef}/appointments", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await setup.GrantAsync(subject, "CenterAppointmentsManage", centerRef, cancellationToken);
        await setup.GrantAsync(staleSubject, "CenterAppointmentsManage", centerRef, cancellationToken,
            currentEvidence: false);
        using var staleCenterClient = setup.AuthenticatedClient(
            staleSubject, centerRef, "CenterAppointmentsManage");
        var staleGrant = await staleCenterClient.GetAsync(
            $"/api/v1/centers/{centerRef}/appointments", cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, staleGrant.StatusCode);

        using var centerClient = setup.AuthenticatedClient(
            subject, centerRef, "CenterAppointmentsManage");
        var start = DateTimeOffset.UtcNow.AddDays(5);
        var define = await centerClient.PostAsJsonAsync(
            $"/api/v1/centers/{centerRef}/appointment-slots",
            new
            {
                requestId = Guid.NewGuid(),
                expectedRevision = 0,
                slotRef = "slot-http-1",
                doctorRef = "doctor-opaque-1",
                startsAt = start,
                endsAt = start.AddMinutes(30)
            }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, define.StatusCode);

        var centerSlots = await centerClient.GetAsync(
            $"/api/v1/centers/{centerRef}/appointment-slots", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, centerSlots.StatusCode);
        using var slotsJson = await centerSlots.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        Assert.Contains(slotsJson!.RootElement.GetProperty("slots").EnumerateArray(),
            item => item.GetProperty("slotRef").GetString() == "slot-http-1");

        var appointments = await centerClient.GetAsync(
            $"/api/v1/centers/{centerRef}/appointments", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, appointments.StatusCode);

        var crossCapability = await centerClient.GetAsync(
            $"/api/v1/member/members/member-ungranted/centers/{centerRef}/appointment-slots",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, crossCapability.StatusCode);
    }

    [Fact]
    public async Task MemberCanBookAndCancelOnlyWithinItsGrantedMemberScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = new AppointmentApiFixture(cancellationToken);
        var centerRef = "center-member-" + Guid.NewGuid().ToString("N");
        var memberRef = "member-http-" + Guid.NewGuid().ToString("N");
        var otherMemberRef = "member-other-" + Guid.NewGuid().ToString("N");
        var centerSubject = "center-subject-" + Guid.NewGuid().ToString("N");
        var memberSubject = "member-subject-" + Guid.NewGuid().ToString("N");
        var secondSubject = "second-member-subject-" + Guid.NewGuid().ToString("N");

        await setup.GrantAsync(centerSubject, "CenterAppointmentsManage", centerRef, cancellationToken);
        await setup.GrantAsync(memberSubject, "MemberAppointmentsManage", memberRef, cancellationToken);
        await setup.GrantAsync(secondSubject, "MemberAppointmentsManage", otherMemberRef, cancellationToken);

        using var centerClient = setup.AuthenticatedClient(
            centerSubject, centerRef, "CenterAppointmentsManage");
        using var memberClient = setup.AuthenticatedClient(
            memberSubject, memberRef, "MemberAppointmentsManage");
        using var secondClient = setup.AuthenticatedClient(
            secondSubject, otherMemberRef, "MemberAppointmentsManage");

        var startsAt = DateTimeOffset.UtcNow.AddDays(6);
        var define = await centerClient.PostAsJsonAsync(
            $"/api/v1/centers/{centerRef}/appointment-slots",
            new
            {
                requestId = Guid.NewGuid(), expectedRevision = 0,
                slotRef = "member-slot-1", doctorRef = "doctor-opaque-2",
                startsAt, endsAt = startsAt.AddMinutes(30)
            }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, define.StatusCode);

        var path = $"/api/v1/member/members/{memberRef}/centers/{centerRef}";
        var slots = await memberClient.GetAsync(path + "/appointment-slots", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, slots.StatusCode);
        var book = await memberClient.PostAsJsonAsync(path + "/appointments",
            new { requestId = Guid.NewGuid(), expectedRevision = 1, slotRef = "member-slot-1" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, book.StatusCode);
        using var bookingJson = await book.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var bookingRef = bookingJson!.RootElement.GetProperty("appointment")
            .GetProperty("bookingRef").GetGuid();

        var cancelPath = path + $"/appointments/{bookingRef:D}/cancel";
        var otherMemberCannotRead = await secondClient.GetAsync(
            path + "/appointments", cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherMemberCannotRead.StatusCode);

        var cancelled = await memberClient.PostAsJsonAsync(cancelPath,
            new { requestId = Guid.NewGuid(), expectedRevision = 2 }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var changedMemberHint = await memberClient.GetAsync(
            $"/api/v1/member/members/{memberRef}-other/centers/{centerRef}/appointments",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, changedMemberHint.StatusCode);
    }
}

internal sealed class AppointmentApiFixture : IAsyncDisposable
{
    private const string Issuer = "https://appointments.test.invalid/realms/taranom";
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _connectionString;

    internal AppointmentApiFixture(CancellationToken cancellationToken)
    {
        _connectionString = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
            ?? throw new InvalidOperationException("TARANOM_TEST_PG is required for API integration tests.");
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        Factory = new AppointmentApiFactory(_connectionString);
        using var startupClient = Factory.CreateClient(); // Starts host and creates the API schema.
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal AppointmentApiFactory Factory { get; }

    internal HttpClient AuthenticatedClient(string subject, string scope, string capability)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Taranom-Test-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Taranom-Test-Scope", scope);
        client.DefaultRequestHeaders.Add("X-Taranom-Test-Capability", capability);
        return client;
    }

    internal HttpClient AuthenticatedIdentityClient(
        string subject,
        params (string Scope, string Capability)[] grants)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Taranom-Test-Subject", subject);
        foreach (var grant in grants)
        {
            var grantClaim = $"v1|{grant.Scope.Length}|{grant.Scope}|{grant.Capability}";
            client.DefaultRequestHeaders.Add("X-Taranom-Test-Grant", grantClaim);
        }
        return client;
    }

    internal async Task GrantAsync(
        string subject,
        string capability,
        string scopeRef,
        CancellationToken cancellationToken,
        bool currentEvidence = true,
        string grantState = "active")
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO taranom_panel_access_grant
                (issuer, subject, capability, scope_ref, grant_state,
                 session_current, subject_mapping_current, permission_version_current,
                 grant_not_revoked, audit_ready)
            VALUES (@issuer, @subject, @capability, @scope, @grant_state,
                    @current, @current, @current, @current, @current)
            """, connection);
        command.Parameters.AddWithValue("issuer", Issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("capability", capability);
        command.Parameters.AddWithValue("scope", scopeRef);
        command.Parameters.AddWithValue("grant_state", grantState);
        command.Parameters.AddWithValue("current", currentEvidence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal async Task<long> CountGrantAuditEventsAsync(
        string centerRef,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM taranom_panel_access_grant_audit
            WHERE scope_ref = @center_ref
            """, connection);
        command.Parameters.AddWithValue("center_ref", centerRef);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _dataSource.DisposeAsync();
    }
}

internal sealed class AppointmentApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://appointments.test.invalid/realms/taranom";
    private readonly string _connectionString;

    internal AppointmentApiFactory(string connectionString)
    {
        _connectionString = connectionString;
        // Minimal hosting captures environment configuration before the
        // WebApplicationFactory web-host callbacks run. Provide test-only
        // settings before the API's top-level Program creates its builder.
        Environment.SetEnvironmentVariable("ConnectionStrings__Taranom", connectionString);
        Environment.SetEnvironmentVariable("Taranom__Authentication__Oidc__Authority", Issuer);
        Environment.SetEnvironmentVariable("Taranom__Authentication__Oidc__TrustedIssuer", Issuer);
        Environment.SetEnvironmentVariable("Taranom__Authentication__Oidc__Audience",
            "taranom-appointments-api-test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Taranom", _connectionString);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Taranom"] = _connectionString,
                ["Taranom:Authentication:Oidc:Authority"] = Issuer,
                ["Taranom:Authentication:Oidc:TrustedIssuer"] = Issuer,
                ["Taranom:Authentication:Oidc:Audience"] = "taranom-appointments-api-test"
            }));

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = AppointmentTestAuthenticationHandler.SchemeName;
                options.DefaultAuthenticateScheme = AppointmentTestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = AppointmentTestAuthenticationHandler.SchemeName;
                options.DefaultForbidScheme = AppointmentTestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, AppointmentTestAuthenticationHandler>(
                AppointmentTestAuthenticationHandler.SchemeName, _ => { });
        });
    }
}

internal sealed class AppointmentTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "AppointmentTest";
    private const string Issuer = "https://appointments.test.invalid/realms/taranom";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers["X-Taranom-Test-Subject"].ToString();
        var scope = Request.Headers["X-Taranom-Test-Scope"].ToString();
        var capability = Request.Headers["X-Taranom-Test-Capability"].ToString();
        if (string.IsNullOrWhiteSpace(subject))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new Claim("iss", Issuer),
            new Claim("sub", subject)
        };
        if (!string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(capability))
        {
            var grant = $"v1|{scope.Length}|{scope}|{capability}";
            claims.Add(new Claim(PanelAccessPolicy.GrantClaimType, grant, ClaimValueTypes.String, Issuer));
        }
        foreach (var grant in Request.Headers["X-Taranom-Test-Grant"])
            if (!string.IsNullOrWhiteSpace(grant))
                claims.Add(new Claim(PanelAccessPolicy.GrantClaimType, grant,
                    ClaimValueTypes.String, Issuer));
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
