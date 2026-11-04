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

    internal async Task GrantAsync(
        string subject,
        string capability,
        string scopeRef,
        CancellationToken cancellationToken,
        bool currentEvidence = true)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO taranom_panel_access_grant
                (issuer, subject, capability, scope_ref, grant_state,
                 session_current, subject_mapping_current, permission_version_current,
                 grant_not_revoked, audit_ready)
            VALUES (@issuer, @subject, @capability, @scope, 'active',
                    @current, @current, @current, @current, @current)
            """, connection);
        command.Parameters.AddWithValue("issuer", Issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("capability", capability);
        command.Parameters.AddWithValue("scope", scopeRef);
        command.Parameters.AddWithValue("current", currentEvidence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _dataSource.DisposeAsync();
    }
}

internal sealed class AppointmentApiFactory(string connectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Taranom", connectionString);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Taranom"] = connectionString,
                ["Taranom:Authentication:Oidc:Authority"] =
                    "https://appointments.test.invalid/realms/taranom",
                ["Taranom:Authentication:Oidc:TrustedIssuer"] =
                    "https://appointments.test.invalid/realms/taranom",
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
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(scope) ||
            string.IsNullOrWhiteSpace(capability))
            return Task.FromResult(AuthenticateResult.NoResult());

        var grant = $"v1|{scope.Length}|{scope}|{capability}";
        var claims = new[]
        {
            new Claim("iss", Issuer),
            new Claim("sub", subject),
            new Claim(PanelAccessPolicy.GrantClaimType, grant, ClaimValueTypes.String, Issuer)
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
