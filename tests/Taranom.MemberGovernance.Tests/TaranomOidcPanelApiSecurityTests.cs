using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Taranom.PanelAccess.AspNetCore;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class TaranomOidcPanelApiSecurityTests
{
    [Fact]
    public void ValidHttpsConfigurationRegistersStrictBearerValidationAndDenyByDefault()
    {
        using var provider = CreateProvider(
            "https://login.example.test/realms/taranom",
            "https://login.example.test/realms/taranom",
            "taranom-api");

        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var authorization = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.Equal("https://login.example.test/realms/taranom", bearer.Authority);
        Assert.Equal("taranom-api", bearer.Audience);
        Assert.True(bearer.RequireHttpsMetadata);
        Assert.False(bearer.MapInboundClaims);
        Assert.False(bearer.SaveToken);
        Assert.False(bearer.IncludeErrorDetails);
        Assert.True(bearer.TokenValidationParameters.ValidateIssuer);
        Assert.Equal("https://login.example.test/realms/taranom",
            bearer.TokenValidationParameters.ValidIssuer);
        Assert.True(bearer.TokenValidationParameters.ValidateAudience);
        Assert.Equal("taranom-api", bearer.TokenValidationParameters.ValidAudience);
        Assert.True(bearer.TokenValidationParameters.ValidateLifetime);
        Assert.True(bearer.TokenValidationParameters.RequireSignedTokens);
        Assert.True(bearer.TokenValidationParameters.ValidateIssuerSigningKey);
        var defaultPolicy = authorization.GetDefaultPolicyAsync().GetAwaiter().GetResult();
        var fallbackPolicy = authorization.GetFallbackPolicyAsync().GetAwaiter().GetResult();
        Assert.NotNull(defaultPolicy);
        Assert.NotNull(fallbackPolicy);
        Assert.Contains(defaultPolicy.Requirements,
            requirement => requirement is UnboundPanelEndpointRequirement);
        Assert.Contains(fallbackPolicy.Requirements,
            requirement => requirement is UnboundPanelEndpointRequirement);
        Assert.IsType<DisconnectedPanelAccessEvidenceSource>(
            provider.GetRequiredService<IPanelAccessEvidenceSource>());
    }

    [Theory]
    [InlineData("http://login.example.test/realms/taranom", "https://login.example.test/realms/taranom", "taranom-api")]
    [InlineData("https://login.example.test/realms/taranom", "http://login.example.test/realms/taranom", "taranom-api")]
    [InlineData("https://login.example.test/realms/taranom", "https://login.example.test/realms/taranom", "")]
    [InlineData("https://user:password@login.example.test/realm", "https://login.example.test/realm", "taranom-api")]
    [InlineData("https://login.example.test/realm?tenant=other", "https://login.example.test/realm", "taranom-api")]
    public void InvalidOrIncompleteTrustConfigurationFailsAtRegistration(
        string authority,
        string trustedIssuer,
        string audience)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            TaranomOidcPanelApiSecurity.AddTaranomOidcPanelApiSecurity(
                services,
                Configuration(authority, trustedIssuer, audience)));

        Assert.Equal(TaranomOidcPanelApiSecurity.ConfigurationSection,
            exception.OptionsName);
    }

    [Fact]
    public void MissingOidcConfigurationFailsAtRegistration()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            TaranomOidcPanelApiSecurity.AddTaranomOidcPanelApiSecurity(
                services,
                new ConfigurationBuilder().Build()));
    }

    private static ServiceProvider CreateProvider(
        string authority,
        string trustedIssuer,
        string audience)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        TaranomOidcPanelApiSecurity.AddTaranomOidcPanelApiSecurity(
            services,
            Configuration(authority, trustedIssuer, audience));
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(
        string authority,
        string trustedIssuer,
        string audience) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TaranomOidcPanelApiSecurity.ConfigurationSection}:Authority"] = authority,
                [$"{TaranomOidcPanelApiSecurity.ConfigurationSection}:TrustedIssuer"] = trustedIssuer,
                [$"{TaranomOidcPanelApiSecurity.ConfigurationSection}:Audience"] = audience
            })
            .Build();
}
