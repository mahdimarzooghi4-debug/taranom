using System.Security.Claims;
using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class PanelAccessPolicyTests
{
    private const string Issuer = "https://identity.example.test/";
    private const string Scope = "center:alpha|west";

    [Fact]
    public void CurrentCentralGrantAndExactTrustedIdentityAreAllowedWithoutPermissionTokenClaims()
    {
        var principal = Principal(Issuer, "staff-1");
        var decision = PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), ScopeContext(Scope),
            PanelCapability.CenterRevenueRead);

        Assert.True(decision.Allowed);
        Assert.Equal("EXACT_TRUSTED_IDENTITY_AND_SERVER_GRANT", decision.Reason);
    }

    [Fact]
    public void DifferentIssuerIsDenied()
    {
        Assert.False(PanelAccessPolicy.Evaluate(
            Principal("https://untrusted.example.test/", "staff-1"), Issuer,
            Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void MissingSubjectIsDenied()
    {
        Assert.False(PanelAccessPolicy.Evaluate(
            Principal(Issuer, null), Issuer, Freshness(), ScopeContext(Scope),
            PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void RoleClaimAloneCannotAuthorize()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin", ClaimValueTypes.String, Issuer)],
            authenticationType: "test-authentication");
        Assert.False(PanelAccessPolicy.Evaluate(
            new ClaimsPrincipal(identity), Issuer, Freshness(), ScopeContext(Scope),
            PanelCapability.TaranomFundRead).Allowed);
    }

    [Fact]
    public void UnauthenticatedIdentityCannotAuthorize()
    {
        Assert.False(PanelAccessPolicy.Evaluate(
            Principal(Issuer, "staff-1", authenticated: false), Issuer,
            Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingTrustedIssuerFailsClosed(string? issuer)
    {
        var decision = PanelAccessPolicy.Evaluate(
            Principal(Issuer, "staff-1"), issuer, Freshness(), ScopeContext(Scope),
            PanelCapability.CenterRevenueRead);
        Assert.False(decision.Allowed);
        Assert.Equal("TRUSTED_ISSUER_NOT_CONFIGURED", decision.Reason);
    }

    [Fact]
    public void MissingFreshnessEvidenceDenies()
    {
        var decision = PanelAccessPolicy.Evaluate(
            Principal(Issuer, "staff-1"), Issuer, null, ScopeContext(Scope),
            PanelCapability.CenterRevenueRead);
        Assert.False(decision.Allowed);
        Assert.Equal("AUTHORIZATION_FRESHNESS_UNAVAILABLE", decision.Reason);
    }

    [Theory]
    [InlineData("session", 0, "SESSION_NOT_CURRENT")]
    [InlineData("subject", 0, "SUBJECT_MAPPING_NOT_CURRENT")]
    [InlineData("permission", 0, "PERMISSION_VERSION_NOT_CURRENT")]
    [InlineData("revocation", 0, "GRANT_REVOCATION_NOT_CONFIRMED")]
    [InlineData("audit", 0, "AUDIT_NOT_READY")]
    public void MissingOrRejectedFreshnessEvidenceDenies(
        string name, int state, string expectedReason)
    {
        var evidence = (PanelEvidenceState)state;
        var freshness = name switch
        {
            "session" => Freshness() with { SessionCurrent = evidence },
            "subject" => Freshness() with { SubjectMappingCurrent = evidence },
            "permission" => Freshness() with { PermissionVersionCurrent = evidence },
            "revocation" => Freshness() with { GrantNotRevoked = evidence },
            "audit" => Freshness() with { AuditReady = evidence },
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var decision = PanelAccessPolicy.Evaluate(
            Principal(Issuer, "staff-1"), Issuer, freshness, ScopeContext(Scope),
            PanelCapability.CenterRevenueRead);
        Assert.False(decision.Allowed);
        Assert.Equal(expectedReason, decision.Reason);
    }

    [Theory]
    [InlineData(1, "MemberOwnPrescriptionRead")]
    [InlineData(2, "CenterRosterManage")]
    [InlineData(3, "CenterAppointmentsManage")]
    [InlineData(4, "CenterRevenueRead")]
    [InlineData(5, "TaranomFundRead")]
    [InlineData(6, "SupportedCaseFinanceRead")]
    [InlineData(7, "MemberAppointmentsManage")]
    [InlineData(8, "CenterAccessGrantsManage")]
    public void CentralCapabilityTokensRemainStable(int value, string expected)
    {
        Assert.Equal(expected, PanelAccessPolicy.GetCapabilityToken((PanelCapability)value));
    }

    [Fact]
    public void UnknownCapabilityFailsClosed()
    {
        Assert.Null(PanelAccessPolicy.GetCapabilityToken((PanelCapability)999));
        Assert.False(PanelAccessPolicy.Evaluate(
            Principal(Issuer, "staff-1"), Issuer, Freshness(), ScopeContext(Scope),
            (PanelCapability)999).Allowed);
    }

    [Fact]
    public void UnresolvedServerScopeDeniesEvenWhenRequestHintMatches()
    {
        var context = new PanelScopeContext(null, Scope, Scope);
        Assert.False(PanelAccessPolicy.Evaluate(
            Principal(Issuer, "staff-1"), Issuer, Freshness(), context,
            PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void RequestHintCannotSelectAnotherScope()
    {
        var context = new PanelScopeContext(Scope, "center:attacker", Scope);
        var decision = PanelAccessPolicy.Evaluate(
            Principal(Issuer, "staff-1"), Issuer, Freshness(), context,
            PanelCapability.CenterRevenueRead);
        Assert.False(decision.Allowed);
        Assert.Equal("REQUEST_SCOPE_HINT_MISMATCH", decision.Reason);
    }

    [Fact]
    public void ResourceOwnerMustMatchServerResolvedScope()
    {
        var context = new PanelScopeContext(Scope, Scope, "center:other");
        Assert.False(PanelAccessPolicy.Evaluate(
            Principal(Issuer, "staff-1"), Issuer, Freshness(), context,
            PanelCapability.CenterRevenueRead).Allowed);
    }

    private static ClaimsPrincipal Principal(
        string issuer, string? subject, bool authenticated = true)
    {
        var claims = new List<Claim> { new("iss", issuer) };
        if (subject is not null)
            claims.Add(new Claim("sub", subject));
        return new ClaimsPrincipal(new ClaimsIdentity(
            claims, authenticated ? "test-authentication" : null));
    }

    private static PanelAccessFreshness Freshness() => new(
        PanelEvidenceState.Confirmed, PanelEvidenceState.Confirmed,
        PanelEvidenceState.Confirmed, PanelEvidenceState.Confirmed,
        PanelEvidenceState.Confirmed);

    private static PanelScopeContext ScopeContext(string scope) =>
        new(scope, scope, scope);
}
