using System.Security.Claims;
using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class PanelAccessPolicyTests
{
    private const string Issuer = "https://identity.example.test/";
    private const string Scope = "center:alpha|west";

    [Fact]
    public void ExactTrustedIssuerCapabilityAndScopeAreAllowed()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        var decision = PanelAccessPolicy.Evaluate(
            principal, Issuer, ScopeContext(Scope), PanelCapability.CenterRevenueRead);

        Assert.True(decision.Allowed);
        Assert.Equal("EXACT_TRUSTED_CAPABILITY_AND_SCOPE", decision.Reason);
    }

    [Fact]
    public void DifferentIssuerIsDenied()
    {
        var principal = Principal(
            Grant(Scope, PanelCapability.CenterRevenueRead),
            "https://untrusted.example.test/");

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void DifferentScopeIsDenied()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, ScopeContext("center:beta"), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void DifferentCapabilityIsDenied()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, ScopeContext(Scope), PanelCapability.CenterAppointmentsManage).Allowed);
    }

    [Fact]
    public void RoleClaimAloneCannotAuthorize()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin", ClaimValueTypes.String, Issuer)],
            authenticationType: "test-authentication");
        var principal = new ClaimsPrincipal(identity);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, ScopeContext(Scope), PanelCapability.TaranomFundRead).Allowed);
    }

    [Fact]
    public void UnauthenticatedIdentityCannotAuthorize()
    {
        var identity = new ClaimsIdentity(
            [new Claim(PanelAccessPolicy.GrantClaimType,
                Grant(Scope, PanelCapability.CenterRevenueRead),
                ClaimValueTypes.String, Issuer)]);
        var principal = new ClaimsPrincipal(identity);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingTrustedIssuerFailsClosed(string? issuer)
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        var decision = PanelAccessPolicy.Evaluate(
            principal, issuer, ScopeContext(Scope), PanelCapability.CenterRevenueRead);

        Assert.False(decision.Allowed);
        Assert.Equal("TRUSTED_ISSUER_NOT_CONFIGURED", decision.Reason);
    }

    [Fact]
    public void UndefinedCapabilityFailsClosed()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, ScopeContext(Scope), (PanelCapability)999).Allowed);
    }

    [Fact]
    public void MalformedOrNonCanonicalGrantFailsClosed()
    {
        var malformed = Principal("v1|4|abc|CenterRevenueRead", Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            malformed, Issuer, ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }


    [Fact]
    public void UnresolvedServerScopeDeniesEvenWhenRequestHintMatches()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var context = new PanelScopeContext(null, Scope, Scope);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, context, PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void RequestHintCannotSelectAnotherScope()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var context = new PanelScopeContext(Scope, "center:attacker", Scope);

        var decision = PanelAccessPolicy.Evaluate(
            principal, Issuer, context, PanelCapability.CenterRevenueRead);

        Assert.False(decision.Allowed);
        Assert.Equal("REQUEST_SCOPE_HINT_MISMATCH", decision.Reason);
    }

    [Fact]
    public void ResourceOwnershipMustMatchServerResolvedScope()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var context = new PanelScopeContext(Scope, Scope, "center:other");

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, context, PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void MatchingRequestHintDoesNotReplaceMissingServerResolution()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var context = new PanelScopeContext(null, Scope, Scope);

        var decision = PanelAccessPolicy.Evaluate(
            principal, Issuer, context, PanelCapability.CenterRevenueRead);

        Assert.False(decision.Allowed);
        Assert.Equal("SERVER_SCOPE_NOT_RESOLVED", decision.Reason);
    }

    private static PanelScopeContext ScopeContext(string? scope) =>
        new(scope, scope, scope);

    private static ClaimsPrincipal Principal(string value, string issuer)
    {
        var claim = new Claim(
            PanelAccessPolicy.GrantClaimType, value, ClaimValueTypes.String, issuer);
        return new ClaimsPrincipal(new ClaimsIdentity([claim], authenticationType: "test-authentication"));
    }

    private static string Grant(string scope, PanelCapability capability) =>
        $"v1|{scope.Length}|{scope}|{capability}";
}
