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
            principal, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead);

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
            principal, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void DifferentScopeIsDenied()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), ScopeContext("center:beta"), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void DifferentCapabilityIsDenied()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterAppointmentsManage).Allowed);
    }

    [Fact]
    public void RoleClaimAloneCannotAuthorize()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin", ClaimValueTypes.String, Issuer)],
            authenticationType: "test-authentication");
        var principal = new ClaimsPrincipal(identity);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.TaranomFundRead).Allowed);
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
            principal, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingTrustedIssuerFailsClosed(string? issuer)
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        var decision = PanelAccessPolicy.Evaluate(
            principal, issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead);

        Assert.False(decision.Allowed);
        Assert.Equal("TRUSTED_ISSUER_NOT_CONFIGURED", decision.Reason);
    }

    [Fact]
    public void MissingFreshnessEvidenceDenies()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        var decision = PanelAccessPolicy.Evaluate(
            principal, Issuer, null, ScopeContext(Scope), PanelCapability.CenterRevenueRead);

        Assert.False(decision.Allowed);
        Assert.Equal("AUTHORIZATION_FRESHNESS_UNAVAILABLE", decision.Reason);
    }

    [Theory]
    [InlineData("session", 0, "SESSION_NOT_CURRENT")]
    [InlineData("session", 2, "SESSION_NOT_CURRENT")]
    [InlineData("subject", 0, "SUBJECT_MAPPING_NOT_CURRENT")]
    [InlineData("subject", 2, "SUBJECT_MAPPING_NOT_CURRENT")]
    [InlineData("permission", 0, "PERMISSION_VERSION_NOT_CURRENT")]
    [InlineData("permission", 2, "PERMISSION_VERSION_NOT_CURRENT")]
    [InlineData("revocation", 0, "GRANT_REVOCATION_NOT_CONFIRMED")]
    [InlineData("revocation", 2, "GRANT_REVOCATION_NOT_CONFIRMED")]
    [InlineData("audit", 0, "AUDIT_NOT_READY")]
    [InlineData("audit", 2, "AUDIT_NOT_READY")]
    public void MissingOrRejectedFreshnessEvidenceDenies(
        string evidenceName, int state, string expectedReason)
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var evidence = (PanelEvidenceState)state;
        var freshness = evidenceName switch
        {
            "session" => Freshness() with { SessionCurrent = evidence },
            "subject" => Freshness() with { SubjectMappingCurrent = evidence },
            "permission" => Freshness() with { PermissionVersionCurrent = evidence },
            "revocation" => Freshness() with { GrantNotRevoked = evidence },
            "audit" => Freshness() with { AuditReady = evidence },
            _ => throw new ArgumentOutOfRangeException(nameof(evidenceName))
        };

        var decision = PanelAccessPolicy.Evaluate(
            principal, Issuer, freshness, ScopeContext(Scope),
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
    public void CapabilityClaimTokensRemainStable(int value, string expected)
    {
        Assert.Equal(expected,
            PanelAccessPolicy.GetCapabilityToken((PanelCapability)value));
    }

    [Fact]
    public void CapabilityWithoutStableTokenFailsClosed()
    {
        Assert.Null(PanelAccessPolicy.GetCapabilityToken((PanelCapability)999));
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), ScopeContext(Scope), (PanelCapability)999).Allowed);
    }

    [Fact]
    public void MalformedOrNonCanonicalGrantFailsClosed()
    {
        var malformed = Principal("v1|4|abc|CenterRevenueRead", Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            malformed, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }


    [Fact]
    public void LengthPrefixWithLeadingZeroIsDenied()
    {
        var canonicalGrant = Grant(Scope, PanelCapability.CenterRevenueRead);
        var noncanonicalGrant = canonicalGrant.Replace(
            $"v1|{Scope.Length}|", $"v1|0{Scope.Length}|", StringComparison.Ordinal);
        var principal = Principal(noncanonicalGrant, Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void OversizedGrantValueIsDenied()
    {
        var principal = Principal("v1|" + new string('x', 1024), Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void CapabilityTokensAreCaseSensitive()
    {
        var principal = Principal(
            $"v1|{Scope.Length}|{Scope}|centerRevenueRead", Issuer);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), ScopeContext(Scope), PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void UnresolvedServerScopeDeniesEvenWhenRequestHintMatches()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var context = new PanelScopeContext(null, Scope, Scope);

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), context, PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void RequestHintCannotSelectAnotherScope()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var context = new PanelScopeContext(Scope, "center:attacker", Scope);

        var decision = PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), context, PanelCapability.CenterRevenueRead);

        Assert.False(decision.Allowed);
        Assert.Equal("REQUEST_SCOPE_HINT_MISMATCH", decision.Reason);
    }

    [Fact]
    public void ResourceOwnershipMustMatchServerResolvedScope()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var context = new PanelScopeContext(Scope, Scope, "center:other");

        Assert.False(PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), context, PanelCapability.CenterRevenueRead).Allowed);
    }

    [Fact]
    public void MatchingRequestHintDoesNotReplaceMissingServerResolution()
    {
        var principal = Principal(Grant(Scope, PanelCapability.CenterRevenueRead), Issuer);
        var context = new PanelScopeContext(null, Scope, Scope);

        var decision = PanelAccessPolicy.Evaluate(
            principal, Issuer, Freshness(), context, PanelCapability.CenterRevenueRead);

        Assert.False(decision.Allowed);
        Assert.Equal("SERVER_SCOPE_NOT_RESOLVED", decision.Reason);
    }

    private static PanelAccessFreshness Freshness() => new(
        PanelEvidenceState.Confirmed, PanelEvidenceState.Confirmed,
        PanelEvidenceState.Confirmed, PanelEvidenceState.Confirmed,
        PanelEvidenceState.Confirmed);

    private static PanelScopeContext ScopeContext(string? scope) =>
        new(scope, scope, scope);

    private static ClaimsPrincipal Principal(string value, string issuer)
    {
        var claim = new Claim(
            PanelAccessPolicy.GrantClaimType, value, ClaimValueTypes.String, issuer);
        return new ClaimsPrincipal(new ClaimsIdentity([claim], authenticationType: "test-authentication"));
    }

    private static string Grant(string scope, PanelCapability capability)
    {
        var token = PanelAccessPolicy.GetCapabilityToken(capability)
            ?? throw new ArgumentOutOfRangeException(nameof(capability));
        return $"v1|{scope.Length}|{scope}|{token}";
    }
}
