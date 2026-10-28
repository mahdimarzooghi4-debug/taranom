using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Taranom.MemberGovernance;

namespace Taranom.PanelAccess.AspNetCore;

internal static class PanelAccessPolicyNames
{
    internal const string MemberOwnPrescriptionRead = "panel.member.own-prescription.read";
    internal const string CenterRosterManage = "panel.center.roster.manage";
    internal const string CenterAppointmentsManage = "panel.center.appointments.manage";
    internal const string CenterRevenueRead = "panel.center.revenue.read";
    internal const string TaranomFundRead = "panel.taranom.fund.read";
    internal const string SupportedCaseFinanceRead = "panel.supported-case.finance.read";
}

internal sealed class PanelAccessOptions
{
    public string? TrustedIssuer { get; set; }
}

internal readonly record struct PanelAccessEvidence(
    PanelAccessFreshness Freshness,
    PanelScopeContext ScopeContext);

// Implementations must resolve evidence from server-owned authentication,
// grant, audit, and target-resource sources. Request fields are not authority.
internal interface IPanelAccessEvidenceSource
{
    ValueTask<PanelAccessEvidence?> ResolveAsync(
        ClaimsPrincipal principal,
        PanelCapability capability,
        object? authorizationResource);
}

internal sealed class DisconnectedPanelAccessEvidenceSource : IPanelAccessEvidenceSource
{
    public ValueTask<PanelAccessEvidence?> ResolveAsync(
        ClaimsPrincipal principal,
        PanelCapability capability,
        object? authorizationResource) =>
        ValueTask.FromResult<PanelAccessEvidence?>(null);
}

internal sealed class PanelAccessRequirement(
    PanelCapability capability) : IAuthorizationRequirement
{
    internal PanelCapability Capability { get; } = capability;
}

internal sealed class PanelAccessAuthorizationHandler(
    IPanelAccessEvidenceSource evidenceSource,
    IOptions<PanelAccessOptions> options)
    : AuthorizationHandler<PanelAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PanelAccessRequirement requirement)
    {
        PanelAccessEvidence? evidence;
        try
        {
            evidence = await evidenceSource.ResolveAsync(
                context.User, requirement.Capability, context.Resource);
        }
        catch (Exception)
        {
            // Provider details never escape the authorization boundary.
            return;
        }

        if (evidence is null)
            return;

        var decision = PanelAccessPolicy.Evaluate(
            context.User,
            options.Value.TrustedIssuer,
            evidence.Value.Freshness,
            evidence.Value.ScopeContext,
            requirement.Capability);
        if (decision.Allowed)
            context.Succeed(requirement);
    }
}

internal static class PanelAccessServiceCollectionExtensions
{
    internal static IServiceCollection AddTaranomPanelAuthorization(
        this IServiceCollection services)
    {
        services.AddOptions<PanelAccessOptions>();
        services.TryAddSingleton<IPanelAccessEvidenceSource,
            DisconnectedPanelAccessEvidenceSource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationHandler, PanelAccessAuthorizationHandler>());

        services.AddAuthorization(options =>
        {
            AddPolicy(options, PanelAccessPolicyNames.MemberOwnPrescriptionRead,
                PanelCapability.MemberOwnPrescriptionRead);
            AddPolicy(options, PanelAccessPolicyNames.CenterRosterManage,
                PanelCapability.CenterRosterManage);
            AddPolicy(options, PanelAccessPolicyNames.CenterAppointmentsManage,
                PanelCapability.CenterAppointmentsManage);
            AddPolicy(options, PanelAccessPolicyNames.CenterRevenueRead,
                PanelCapability.CenterRevenueRead);
            AddPolicy(options, PanelAccessPolicyNames.TaranomFundRead,
                PanelCapability.TaranomFundRead);
            AddPolicy(options, PanelAccessPolicyNames.SupportedCaseFinanceRead,
                PanelCapability.SupportedCaseFinanceRead);
        });
        return services;
    }

    private static void AddPolicy(
        AuthorizationOptions options,
        string name,
        PanelCapability capability) =>
        options.AddPolicy(name, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new PanelAccessRequirement(capability));
        });
}
