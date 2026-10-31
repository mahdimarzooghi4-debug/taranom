using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Taranom.MemberGovernance;

namespace Taranom.PanelAccess.AspNetCore;

internal static class PanelAccessEndpointConventionExtensions
{
    // Binds an endpoint directly to the owner-defined capability catalog so
    // route code cannot copy policy strings or silently map an unknown scope.
    internal static IEndpointConventionBuilder RequirePanelCapability(
        this IEndpointConventionBuilder builder,
        PanelCapability capability)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var definition = PanelAccessPolicyNames.All
            .SingleOrDefault(item => item.Capability == capability);
        if (definition.Name is null)
            throw new ArgumentOutOfRangeException(
                nameof(capability), capability, "PANEL_CAPABILITY_NOT_REGISTERED");

        return builder.RequireAuthorization(definition.Name);
    }
}
