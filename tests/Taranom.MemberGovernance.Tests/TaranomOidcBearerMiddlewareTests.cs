using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Taranom.PanelAccess.AspNetCore;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class TaranomOidcBearerMiddlewareTests
{
    private const string Issuer = "https://identity.test.invalid/realms/taranom";
    private const string Audience = "taranom-api-test";

    [Fact]
    public async Task ValidSignedTokenPassesTheConfiguredBearerMiddleware()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var trustedKey = RSA.Create();
        trustedKey.KeySize = 2048;
        using var host = StartHost(trustedKey);
        using var client = host.GetTestClient();

        using var anonymous = await client.GetAsync("/identity", cancellationToken);
        Assert.Equal(StatusCodes.Status401Unauthorized, (int)anonymous.StatusCode);

        var token = CreateToken(trustedKey, Issuer, Audience, DateTime.UtcNow.AddMinutes(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/identity");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var accepted = await client.SendAsync(request, cancellationToken);
        Assert.Equal(StatusCodes.Status200OK, (int)accepted.StatusCode);
        var payload = await accepted.Content.ReadFromJsonAsync<IdentityPayload>(cancellationToken);
        Assert.Equal(Issuer, payload?.Issuer);
        Assert.Equal("member-opaque-1", payload?.Subject);
    }

    [Theory]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-key")]
    [InlineData("expired")]
    [InlineData("unsigned")]
    public async Task InvalidTokenIsRejectedByTheConfiguredBearerMiddleware(string failure)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var trustedKey = RSA.Create();
        trustedKey.KeySize = 2048;
        using var otherKey = RSA.Create();
        otherKey.KeySize = 2048;
        using var host = StartHost(trustedKey);
        using var client = host.GetTestClient();

        var issuer = failure == "wrong-issuer" ? "https://attacker.test.invalid/realm" : Issuer;
        var audience = failure == "wrong-audience" ? "other-api" : Audience;
        var key = failure == "wrong-key" ? otherKey : trustedKey;
        var expires = failure == "expired" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5);
        var signed = failure != "unsigned";
        var token = CreateToken(key, issuer, audience, expires, signed);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/identity");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(StatusCodes.Status401Unauthorized, (int)response.StatusCode);
    }

    private static IHost StartHost(RSA trustedRsa)
    {
        var oidc = new OpenIdConnectConfiguration { Issuer = Issuer };
        oidc.SigningKeys.Add(new RsaSecurityKey(trustedRsa));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TaranomOidcPanelApiSecurity.ConfigurationSection}:Authority"] = Issuer,
                [$"{TaranomOidcPanelApiSecurity.ConfigurationSection}:TrustedIssuer"] = Issuer,
                [$"{TaranomOidcPanelApiSecurity.ConfigurationSection}:Audience"] = Audience
            })
            .Build();

        return new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddTaranomOidcPanelApiSecurity(configuration);
                    services.PostConfigure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme,
                        options => options.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc));
                });
                webHost.Configure(app =>
                {
                    app.UseAuthentication();
                    app.Run(async context =>
                    {
                        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
                        if (!result.Succeeded || result.Principal is null)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status200OK;
                        var identity = result.Principal.Identities.Single();
                        await context.Response.WriteAsJsonAsync(new IdentityPayload(
                            identity.FindFirst("iss")?.Value,
                            identity.FindFirst("sub")?.Value));
                    });
                });
            })
            .Start();
    }

    private static string CreateToken(
        RSA rsa,
        string issuer,
        string audience,
        DateTime expires,
        bool signed = true)
    {
        var credentials = signed
            ? new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
            : null;
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", "member-opaque-1")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: expires,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record IdentityPayload(string? Issuer, string? Subject);
}
