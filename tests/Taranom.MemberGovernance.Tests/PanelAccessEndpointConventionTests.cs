using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class PanelAccessEndpointConventionTests
{
    private const string Issuer = "https://identity.example.test/";
    private const string Scope = "test:panel-resource";

    [Fact]
    public async Task HttpEndpointsEnforceTheExactCapabilityFromTheCentralCatalog()
    {
        var definitions = PanelAccessPolicyNames.All;
        Assert.Equal(6, definitions.Count);

        foreach (var authenticatedCapability in definitions.Select(item => item.Capability))
        {
            await using var host = await StartHostAsync(authenticatedCapability);
            foreach (var required in definitions)
            {
                using var response = await host.Client.GetAsync(
                    $"/panel-capability/{(int)required.Capability}",
                    TestContext.Current.CancellationToken);
                var expected = required.Capability == authenticatedCapability
                    ? HttpStatusCode.NoContent
                    : HttpStatusCode.Forbidden;
                Assert.Equal(expected, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task UnboundEndpointsDenyAuthenticatedRequestsAndChallengeAnonymousRequests()
    {
        var routes = new[]
        {
            "/panel-unbound-fallback",
            "/panel-unbound-explicit"
        };

        await using (var authenticatedHost = await StartHostAsync(
                         PanelCapability.CenterRevenueRead))
        {
            foreach (var route in routes)
            {
                using var response = await authenticatedHost.Client.GetAsync(
                    route, TestContext.Current.CancellationToken);
                var content = await response.Content.ReadAsStringAsync(
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.DoesNotContain("endpoint reached", content);
            }
        }

        await using (var anonymousHost = await StartHostAsync(
                         PanelCapability.CenterRevenueRead, authenticated: false))
        {
            foreach (var route in routes)
            {
                using var response = await anonymousHost.Client.GetAsync(
                    route, TestContext.Current.CancellationToken);
                var content = await response.Content.ReadAsStringAsync(
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.DoesNotContain("endpoint reached", content);
            }
        }
    }

    [Fact]
    public async Task EndpointRejectsCapabilityMissingFromThePolicyCatalog()
    {
        await using var app = WebApplication.CreateBuilder().Build();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            app.MapGet("/invalid", () => Results.NoContent())
                .RequirePanelCapability((PanelCapability)999));
    }

    private static async Task<TestHost> StartHostAsync(
        PanelCapability authenticatedCapability,
        bool authenticated = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "synthetic-test";
                options.DefaultChallengeScheme = "synthetic-test";
            })
            .AddScheme<SyntheticAuthenticationOptions, SyntheticAuthenticationHandler>(
                "synthetic-test",
                options =>
                {
                    options.Capability = authenticatedCapability;
                    options.Authenticated = authenticated;
                });
        builder.Services.AddSingleton<IPanelAccessEvidenceSource, SyntheticEvidenceSource>();
        builder.Services.Configure<PanelAccessOptions>(
            options => options.TrustedIssuer = Issuer);
        builder.Services.AddTaranomPanelAuthorization();

        var app = builder.Build();
        app.UseTaranomPanelAuthorization();
        foreach (var definition in PanelAccessPolicyNames.All)
        {
            app.MapGet(
                    $"/panel-capability/{(int)definition.Capability}",
                    () => Results.NoContent())
                .RequirePanelCapability(definition.Capability);
        }
        app.MapGet("/panel-unbound-fallback", () => Results.Text("endpoint reached"));
        app.MapGet("/panel-unbound-explicit", () => Results.Text("endpoint reached"))
            .RequireAuthorization();

        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses.Single()
                ?? throw new InvalidOperationException("TEST_SERVER_ADDRESS_MISSING");
            return new TestHost(app, new HttpClient { BaseAddress = new Uri(address) });
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private sealed class TestHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        internal HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class SyntheticAuthenticationOptions : AuthenticationSchemeOptions
    {
        internal PanelCapability Capability { get; set; }
        internal bool Authenticated { get; set; } = true;
    }

    private sealed class SyntheticAuthenticationHandler(
        IOptionsMonitor<SyntheticAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<SyntheticAuthenticationOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Options.Authenticated)
                return Task.FromResult(AuthenticateResult.NoResult());

            var token = PanelAccessPolicy.GetCapabilityToken(Options.Capability)!;
            var claim = new Claim(
                PanelAccessPolicy.GrantClaimType,
                $"v1|{Scope.Length}|{Scope}|{token}",
                ClaimValueTypes.String,
                Issuer);
            var identity = new ClaimsIdentity([claim], Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class SyntheticEvidenceSource : IPanelAccessEvidenceSource
    {
        public ValueTask<PanelAccessEvidence?> ResolveAsync(
            ClaimsPrincipal principal,
            PanelCapability capability,
            object? authorizationResource)
        {
            var freshness = new PanelAccessFreshness(
                PanelEvidenceState.Confirmed,
                PanelEvidenceState.Confirmed,
                PanelEvidenceState.Confirmed,
                PanelEvidenceState.Confirmed,
                PanelEvidenceState.Confirmed);
            var scope = new PanelScopeContext(Scope, Scope, Scope);
            return ValueTask.FromResult<PanelAccessEvidence?>(
                new PanelAccessEvidence(freshness, scope));
        }
    }
}
