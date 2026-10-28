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

// Values must come from server-side resource resolution. RequestScopeHint is
// untrusted input and can only be checked against that resolved resource scope.
internal readonly record struct PanelScopeContext(
    string? ServerResolvedScopeRef,
    string? RequestScopeHint,
    string? ResourceOwnerScopeRef);

internal readonly record struct PanelAccessDecision(bool Allowed, string Reason);

internal static class PanelAccessPolicy
{
    internal const string GrantClaimType = "urn:taranom:panel-grant:v1";
    private const int MaximumScopeLength = 256;
    private const string GrantPrefix = "v1|";

    // The caller must pass a ClaimsPrincipal produced by the server's
    // configured authentication handler and a scope context produced by the
    // server's resource resolver. Constructing either from request parameters
    // does not establish identity, ownership, or authority.
    // Sprint 245 configures no handler, resolver, issuer, endpoint, or gate.
    internal static PanelAccessDecision Evaluate(
        ClaimsPrincipal? principal,
        string? trustedIssuer,
        PanelScopeContext? scopeContext,
        PanelCapability capability)
    {
        if (principal is null)
            return new(false, "IDENTITY_MISSING");
        if (string.IsNullOrWhiteSpace(trustedIssuer))
            return new(false, "TRUSTED_ISSUER_NOT_CONFIGURED");
        var capabilityToken = GetCapabilityToken(capability);
        if (capabilityToken is null)
            return new(false, "INVALID_PANEL_CAPABILITY");
        if (scopeContext is null ||
            !IsValidScope(scopeContext.Value.ServerResolvedScopeRef))
            return new(false, "SERVER_SCOPE_NOT_RESOLVED");

        var scope = scopeContext.Value;
        if (!IsValidScope(scope.ResourceOwnerScopeRef) ||
            !string.Equals(scope.ServerResolvedScopeRef,
                scope.ResourceOwnerScopeRef, StringComparison.Ordinal))
            return new(false, "RESOURCE_SCOPE_MISMATCH");

        if (scope.RequestScopeHint is not null &&
            (!IsValidScope(scope.RequestScopeHint) ||
             !string.Equals(scope.RequestScopeHint,
                 scope.ServerResolvedScopeRef, StringComparison.Ordinal)))
            return new(false, "REQUEST_SCOPE_HINT_MISMATCH");

        foreach (var identity in principal.Identities)
        {
            if (!identity.IsAuthenticated)
                continue;

            foreach (var claim in identity.FindAll(GrantClaimType))
            {
                if (!string.Equals(claim.Issuer, trustedIssuer, StringComparison.Ordinal))
                    continue;

                if (TryReadGrant(claim.Value, out var scopeRef, out var claimedCapability) &&
                    string.Equals(scopeRef, scope.ServerResolvedScopeRef, StringComparison.Ordinal) &&
                    claimedCapability == capability)
                    return new(true, "EXACT_TRUSTED_CAPABILITY_AND_SCOPE");
            }
        }

        return new(false, "CAPABILITY_SCOPE_OR_ISSUER_DENIED");
    }

    // These tokens are the v1 claim contract. Keep them explicit so renaming
    // an internal enum member cannot silently change values already in tokens.
    internal static string? GetCapabilityToken(PanelCapability capability) =>
        capability switch
        {
            PanelCapability.MemberOwnPrescriptionRead => "MemberOwnPrescriptionRead",
            PanelCapability.CenterRosterManage => "CenterRosterManage",
            PanelCapability.CenterAppointmentsManage => "CenterAppointmentsManage",
            PanelCapability.CenterRevenueRead => "CenterRevenueRead",
            PanelCapability.TaranomFundRead => "TaranomFundRead",
            PanelCapability.SupportedCaseFinanceRead => "SupportedCaseFinanceRead",
            _ => null
        };

    private static bool TryParseCapabilityToken(
        string value,
        out PanelCapability capability)
    {
        capability = value switch
        {
            "MemberOwnPrescriptionRead" => PanelCapability.MemberOwnPrescriptionRead,
            "CenterRosterManage" => PanelCapability.CenterRosterManage,
            "CenterAppointmentsManage" => PanelCapability.CenterAppointmentsManage,
            "CenterRevenueRead" => PanelCapability.CenterRevenueRead,
            "TaranomFundRead" => PanelCapability.TaranomFundRead,
            "SupportedCaseFinanceRead" => PanelCapability.SupportedCaseFinanceRead,
            _ => default
        };
        return GetCapabilityToken(capability) is not null &&
            string.Equals(GetCapabilityToken(capability), value, StringComparison.Ordinal);
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
        out PanelCapability capability)
    {
        scopeRef = string.Empty;
        capability = default;

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
        if (!TryParseCapabilityToken(parsedCapability, out capability))
            return false;

        scopeRef = value.Substring(scopeStart, scopeLength);
        return true;
    }
}
