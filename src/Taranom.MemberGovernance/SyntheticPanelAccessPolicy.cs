using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// Capability names describe panel data/actions without fixing product roles or
// their hierarchy. These grants are synthetic fixtures, never real authority.
internal enum SyntheticPanelCapability
{
    MemberOwnPrescriptionRead = 1,
    CenterRosterManage = 2,
    CenterAppointmentsManage = 3,
    CenterRevenueRead = 4,
    TaranomFundRead = 5,
    SupportedCaseFinanceRead = 6
}

internal readonly record struct SyntheticPanelGrant(
    string ScopeRef,
    SyntheticPanelCapability Capability);

internal sealed class SyntheticPanelPrincipal
{
    private SyntheticPanelPrincipal(
        string actorRef,
        ImmutableArray<SyntheticPanelGrant> grants,
        bool trusted)
    {
        ActorRef = actorRef;
        Grants = grants;
        Trusted = trusted;
    }

    internal string ActorRef { get; }
    internal ImmutableArray<SyntheticPanelGrant> Grants { get; }
    internal bool Trusted { get; }

    internal static SyntheticPanelPrincipal ForTest(
        string? actorRef,
        IEnumerable<SyntheticPanelGrant>? grants)
    {
        if (!TestRef(actorRef) || grants is null)
            return new SyntheticPanelPrincipal(actorRef ?? string.Empty,
                ImmutableArray<SyntheticPanelGrant>.Empty, false);

        var copy = grants.ToImmutableArray();
        var trusted = copy.All(grant =>
            TestRef(grant.ScopeRef) &&
            Enum.IsDefined(grant.Capability));
        return new SyntheticPanelPrincipal(actorRef!, copy, trusted);
    }

    private static bool TestRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}

internal readonly record struct SyntheticPanelAccessDecision(
    bool Allowed,
    string Reason);

internal static class SyntheticPanelAccessPolicy
{
    internal static SyntheticPanelAccessDecision Evaluate(
        SyntheticPanelPrincipal? principal,
        string? requestedScopeRef,
        SyntheticPanelCapability capability)
    {
        if (principal is null || !principal.Trusted)
            return new(false, "UNTRUSTED_ORIGIN");
        if (requestedScopeRef is not { Length: > 5 and <= 128 } ||
            !requestedScopeRef.StartsWith("test:", StringComparison.Ordinal) ||
            !Enum.IsDefined(capability))
            return new(false, "INVALID_SYNTHETIC_PANEL_REQUEST");

        var allowed = principal.Grants.Any(grant =>
            string.Equals(grant.ScopeRef, requestedScopeRef, StringComparison.Ordinal) &&
            grant.Capability == capability);
        return allowed
            ? new(true, "EXACT_TEST_CAPABILITY")
            : new(false, "CAPABILITY_OR_SCOPE_DENIED");
    }
}
