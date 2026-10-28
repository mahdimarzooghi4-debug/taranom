using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class PanelAccessAuthorizationHandlerTests
{
    private const string Issuer = "https://identity.example.test/";
    private const string Scope = "center:alpha";

    [Fact]
    public async Task ExactEvidenceAuthorizesThroughAspNetAuthorizationService()
    {
        var resource = new object();
        var source = new FixedEvidenceSource(Evidence(), resource);
        using var services = BuildServices(source, Issuer);
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            Principal(PanelCapability.CenterRevenueRead),
            resource,
            PanelAccessPolicyNames.CenterRevenueRead);

        Assert.True(result.Succeeded);
        Assert.Same(resource, source.LastResource);
    }

    [Fact]
    public async Task MissingEvidenceSourceDeniesByDefault()
    {
        using var services = BuildServices(null, Issuer);
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            Principal(PanelCapability.CenterRevenueRead),
            new object(),
            PanelAccessPolicyNames.CenterRevenueRead);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task MissingIssuerConfigurationDenies()
    {
        using var services = BuildServices(new FixedEvidenceSource(Evidence()), null);
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            Principal(PanelCapability.CenterRevenueRead),
            new object(),
            PanelAccessPolicyNames.CenterRevenueRead);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UnauthenticatedPrincipalIsDeniedBeforeEvidenceCanAuthorize()
    {
        using var services = BuildServices(new FixedEvidenceSource(Evidence()), Issuer);
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await authorization.AuthorizeAsync(
            anonymous,
            new object(),
            PanelAccessPolicyNames.CenterRevenueRead);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DifferentCapabilityCannotSatisfyNamedPolicy()
    {
        using var services = BuildServices(new FixedEvidenceSource(Evidence()), Issuer);
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            Principal(PanelCapability.CenterAppointmentsManage),
            new object(),
            PanelAccessPolicyNames.CenterRevenueRead);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task StalePermissionEvidenceDeniesAtAuthorizationBoundary()
    {
        var stale = Evidence() with
        {
            Freshness = Evidence().Freshness with
            {
                PermissionVersionCurrent = PanelEvidenceState.Rejected
            }
        };
        using var services = BuildServices(new FixedEvidenceSource(stale), Issuer);
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            Principal(PanelCapability.CenterRevenueRead),
            new object(),
            PanelAccessPolicyNames.CenterRevenueRead);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task EvidenceSourceFailureDeniesWithoutLeakingDetails()
    {
        using var services = BuildServices(new ThrowingEvidenceSource(), Issuer);
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            Principal(PanelCapability.CenterRevenueRead),
            new object(),
            PanelAccessPolicyNames.CenterRevenueRead);

        Assert.False(result.Succeeded);
    }

    private static ServiceProvider BuildServices(
        IPanelAccessEvidenceSource? source,
        string? issuer)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (source is not null)
            services.AddSingleton(source);
        if (issuer is not null)
            services.Configure<PanelAccessOptions>(options => options.TrustedIssuer = issuer);
        services.AddTaranomPanelAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal(PanelCapability capability)
    {
        var capabilityToken = PanelAccessPolicy.GetCapabilityToken(capability)!;
        var claim = new Claim(
            PanelAccessPolicy.GrantClaimType,
            $"v1|{Scope.Length}|{Scope}|{capabilityToken}",
            ClaimValueTypes.String,
            Issuer);
        return new ClaimsPrincipal(new ClaimsIdentity([claim], "synthetic-test-auth"));
    }

    private static PanelAccessEvidence Evidence()
    {
        var freshness = new PanelAccessFreshness(
            PanelEvidenceState.Confirmed,
            PanelEvidenceState.Confirmed,
            PanelEvidenceState.Confirmed,
            PanelEvidenceState.Confirmed,
            PanelEvidenceState.Confirmed);
        var scope = new PanelScopeContext(Scope, Scope, Scope);
        return new PanelAccessEvidence(freshness, scope);
    }

    private sealed class FixedEvidenceSource(
        PanelAccessEvidence? evidence,
        object? expectedResource = null) : IPanelAccessEvidenceSource
    {
        internal object? LastResource { get; private set; }

        public ValueTask<PanelAccessEvidence?> ResolveAsync(
            ClaimsPrincipal principal,
            PanelCapability capability,
            object? authorizationResource)
        {
            LastResource = authorizationResource;
            if (expectedResource is not null && !ReferenceEquals(
                    expectedResource, authorizationResource))
                return ValueTask.FromResult<PanelAccessEvidence?>(null);
            return ValueTask.FromResult(evidence);
        }
    }

    private sealed class ThrowingEvidenceSource : IPanelAccessEvidenceSource
    {
        public ValueTask<PanelAccessEvidence?> ResolveAsync(
            ClaimsPrincipal principal,
            PanelCapability capability,
            object? authorizationResource) =>
            throw new InvalidOperationException("synthetic provider failure");
    }
}
