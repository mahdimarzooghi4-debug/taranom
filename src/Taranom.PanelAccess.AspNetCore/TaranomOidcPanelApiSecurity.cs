using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Taranom.PanelAccess.AspNetCore;

/// <summary>
/// Adds the real OIDC bearer-token boundary for a Taranom API host.
/// Panel grants and resource scope still come from the server-owned evidence
/// source; authentication by itself never authorizes a panel operation.
/// </summary>
public static class TaranomOidcPanelApiSecurity
{
    public const string ConfigurationSection = "Taranom:Authentication:Oidc";

    /// <summary>
    /// Configures an API to validate tokens from a trusted OIDC issuer and
    /// applies the existing deny-by-default panel capability policies.
    /// The disconnected evidence source remains the default, so protected
    /// operations stay denied until a reviewed server-side grant/scope source
    /// is registered by the API host.
    /// </summary>
    public static IServiceCollection AddTaranomOidcPanelApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration
            .GetSection(ConfigurationSection)
            .Get<TaranomOidcPanelApiSettings>();
        Validate(settings);

        services.Configure<PanelAccessOptions>(options =>
            options.TrustedIssuer = settings!.TrustedIssuer);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings!.Authority;
                options.Audience = settings.Audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.IncludeErrorDetails = false;
                options.RefreshOnIssuerKeyNotFound = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.TrustedIssuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };
            });

        services.AddTaranomPanelAuthorization();
        return services;
    }

    private static void Validate(TaranomOidcPanelApiSettings? settings)
    {
        if (settings is null ||
            !IsHttpsAbsoluteUri(settings.Authority) ||
            !IsHttpsAbsoluteUri(settings.TrustedIssuer) ||
            string.IsNullOrWhiteSpace(settings.Audience) ||
            settings.Audience.Length > 256 ||
            settings.Audience.Any(char.IsControl))
        {
            throw new OptionsValidationException(
                ConfigurationSection,
                typeof(TaranomOidcPanelApiSettings),
                ["An HTTPS OIDC authority, exact HTTPS trusted issuer, and bounded audience must be configured."]);
        }
    }

    private static bool IsHttpsAbsoluteUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);
}

public sealed class TaranomOidcPanelApiSettings
{
    public string? Authority { get; set; }
    public string? TrustedIssuer { get; set; }
    public string? Audience { get; set; }
}
