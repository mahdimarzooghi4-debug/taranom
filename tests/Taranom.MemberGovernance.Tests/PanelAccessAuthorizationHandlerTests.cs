using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Taranom.MemberGovernance;
using Taranom.PanelAccess.AspNetCore;
using System.Text.Json;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class PanelAccessAuthorizationHandlerTests
{
    [Fact]
    public async Task ChallengeUsesStableNoStoreProblemResponseWithoutIdentityDetails()
    {
        using var services = BuildServices(null, null);
        var handler = services.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        await handler.HandleAsync(
            _ => throw new Xunit.Sdk.XunitException("Denied request reached endpoint."),
            context,
            policy,
            PolicyAuthorizationResult.Challenge());

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal("PANEL_AUTHENTICATION_REQUIRED", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("detail", out _));
        Assert.False(body.RootElement.TryGetProperty("scope", out _));
    }

    [Fact]
    public async Task ForbidUsesStableNoStoreProblemResponseWithoutPolicyDetails()
    {
        using var services = BuildServices(null, null);
        var handler = services.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        await handler.HandleAsync(
            _ => throw new Xunit.Sdk.XunitException("Denied request reached endpoint."),
            context,
            policy,
            PolicyAuthorizationResult.Forbid());

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal("PANEL_ACCESS_DENIED", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("policy", out _));
        Assert.False(body.RootElement.TryGetProperty("evidence", out _));
    }

    [Fact]
    public async Task SuccessPassesThroughToEndpoint()
    {
        using var services = BuildServices(null, null);
        var handler = services.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var context = new DefaultHttpContext();
        var called = false;
        var policy = new AuthorizationPolicyBuilder().Build();

        await handler.HandleAsync(
            _ => { called = true; return Task.CompletedTask; },
            context,
            policy,
            PolicyAuthorizationResult.Success());

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

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
