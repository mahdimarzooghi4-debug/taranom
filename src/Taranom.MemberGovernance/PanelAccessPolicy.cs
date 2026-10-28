using System.Security.Claims;

namespace Taranom.MemberGovernance;

// Capability names preserve the owner-confirmed panel scope without defining
// product roles, role hierarchy, or who may issue a grant.
internal enum PanelCapability
{
    MemberOwnPrescriptionRead = 1,
    CenterRosterManage = 2,
    CenterAppointmentsManage = 3,
    CenterRevenueRead = 4,
    TaranomFundRead = 5,
    SupportedCaseFinanceRead = 6
}

internal readonly record struct PanelAccessDecision(bool Allowed, string Reason);

internal static class PanelAccessPolicy
{
    internal const string GrantClaimType = "urn:taranom:panel-grant:v1";
    private const int MaximumScopeLength = 256;
    private const string GrantPrefix = "v1|";

    // The caller must pass a ClaimsPrincipal produced by the server's
    // configured authentication handler. Constructing claims in application
    // code or from request parameters does not establish identity or authority.
    // Sprint 243 configures no handler, issuer, endpoint, or production gate.
    internal static PanelAccessDecision Evaluate(
        ClaimsPrincipal? principal,
        string? trustedIssuer,
        string? requestedScopeRef,
        PanelCapability capability)
    {
        if (principal is null)
            return new(false, "IDENTITY_MISSING");
        if (string.IsNullOrWhiteSpace(trustedIssuer))
            return new(false, "TRUSTED_ISSUER_NOT_CONFIGURED");
        if (!IsValidScope(requestedScopeRef) || !Enum.IsDefined(capability))
            return new(false, "INVALID_PANEL_REQUEST");

        var capabilityName = capability.ToString();
        foreach (var identity in principal.Identities)
        {
            if (!identity.IsAuthenticated)
                continue;

            foreach (var claim in identity.FindAll(GrantClaimType))
            {
                if (!string.Equals(claim.Issuer, trustedIssuer, StringComparison.Ordinal))
                    continue;

                if (TryReadGrant(claim.Value, out var scopeRef, out var claimedCapability) &&
                    string.Equals(scopeRef, requestedScopeRef, StringComparison.Ordinal) &&
                    string.Equals(claimedCapability, capabilityName, StringComparison.Ordinal))
                    return new(true, "EXACT_TRUSTED_CAPABILITY_AND_SCOPE");
            }
        }

        return new(false, "CAPABILITY_SCOPE_OR_ISSUER_DENIED");
    }

    private static bool IsValidScope(string? scopeRef) =>
        scopeRef is { Length: > 0 and <= MaximumScopeLength } &&
        !string.IsNullOrWhiteSpace(scopeRef) &&
        !scopeRef.Any(char.IsControl);

    // Length-prefixing keeps scope references unambiguous even when they
    // contain separators such as ':' or '|'. Only exact canonical claims match.
    private static bool TryReadGrant(
        string? value,
        out string scopeRef,
        out string capability)
    {
        scopeRef = string.Empty;
        capability = string.Empty;

        if (value is null || !value.StartsWith(GrantPrefix, StringComparison.Ordinal))
            return false;

        var lengthEnd = value.IndexOf('|', GrantPrefix.Length);
        if (lengthEnd < 0 ||
            !int.TryParse(value.AsSpan(GrantPrefix.Length, lengthEnd - GrantPrefix.Length),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var scopeLength) ||
            scopeLength is <= 0 or > MaximumScopeLength)
            return false;

        var scopeStart = lengthEnd + 1;
        var capabilitySeparator = scopeStart + scopeLength;
        if (capabilitySeparator >= value.Length ||
            value[capabilitySeparator] != '|' ||
            !IsValidScope(value.Substring(scopeStart, scopeLength)))
            return false;

        var parsedCapability = value[(capabilitySeparator + 1)..];
        if (!Enum.GetNames<PanelCapability>().Contains(parsedCapability, StringComparer.Ordinal))
            return false;

        scopeRef = value.Substring(scopeStart, scopeLength);
        capability = parsedCapability;
        return true;
    }
}
