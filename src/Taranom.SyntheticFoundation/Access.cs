namespace Taranom.SyntheticFoundation;

// Test-only principals have no public constructor, public factory or production authentication capability.
// No production application references this assembly in Sprint 01.
public sealed class SyntheticPrincipal
{
    internal SyntheticPrincipal(string actor, string member, string tenant, string role, bool trusted)
    {
        Actor = actor;
        Member = member;
        Tenant = tenant;
        Role = role;
        Trusted = trusted;
    }

    public string Actor { get; }
    public string Member { get; }
    public string Tenant { get; }
    public string Role { get; }
    internal bool Trusted { get; }
}

public readonly record struct AccessDecision(bool Allowed, string Reason)
{
    public static AccessDecision Denied(string reason) => new(false, reason);
}

public static class SyntheticAccessPolicy
{
    public static AccessDecision Evaluate(
        SyntheticPrincipal? principal, string? subject, string? tenant, string? action)
    {
        if (principal is null || !principal.Trusted)
            return AccessDecision.Denied("UNTRUSTED_ORIGIN");
        if (!IsSynthetic(principal.Actor) || !IsSynthetic(principal.Member) ||
            !IsSynthetic(principal.Tenant) || !IsSynthetic(subject) || !IsSynthetic(tenant))
            return AccessDecision.Denied("INVALID_SYNTHETIC_REFERENCE");
        if (principal.Role != "MEMBER_TEST")
            return AccessDecision.Denied("ROLE_DENIED");
        if (!string.Equals(principal.Member, subject, StringComparison.Ordinal) ||
            !string.Equals(principal.Tenant, tenant, StringComparison.Ordinal))
            return AccessDecision.Denied("SUBJECT_SCOPE_CONFLICT");
        if (action is not ("READ_OWN_TEST_PROFILE" or "READ_OWN_TEST_CONSENT" or "CHANGE_OWN_TEST_CONSENT"))
            return AccessDecision.Denied("ACTION_DENIED");
        return new AccessDecision(true, "EXPLICIT_SYNTHETIC_ALLOW");
    }

    internal static bool IsSynthetic(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("test:", StringComparison.Ordinal) &&
        value.Length <= 128 && !value.Any(char.IsWhiteSpace);
}

public enum SyntheticPurpose
{
    CareCommunicationTest,
    HealthDataShareTest,
    SecondaryAiResearchTest
}

public readonly record struct ConsentMutationResult(int Version, bool Replayed);
