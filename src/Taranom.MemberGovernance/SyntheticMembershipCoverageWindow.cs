namespace Taranom.MemberGovernance;

// Sprint26: hypothetical, caller-provided test-only membership boundaries.
// These are NOT authenticated official events, an external membership adapter,
// consent records, membership approvals or financial posting instructions.
internal enum SyntheticMembershipBoundaryKind
{
    ActivationRecorded = 1,
    NonRenewalRecorded = 2,
    ReactivationRecorded = 3
}

internal readonly record struct SyntheticMembershipBoundary(
    long Revision, Guid EventId, string SubjectRef,
    SyntheticMembershipBoundaryKind Kind, DateTimeOffset EffectiveAt);

internal enum MembershipWindowObservation
{
    InvalidInput = 1,
    InvalidHistory = 2,
    EvidenceInsufficient = 3,
    WithinClosedRecordedMembership = 4,
    WithinClosedRecordedGap = 5,
    CrossesRecordedBoundary = 6
}

// Retrospective time-window classification ONLY. Even a "recorded membership"
// result is not a verified right to money, wallet credit, treatment or access.
// A present/open interval is ALWAYS unknown without a later closing boundary.
// This pure reducer MUST NOT be exposed as a real entitlement/activation API.
internal static class SyntheticMembershipCoverageWindow
{
    internal static MembershipWindowObservation Observe(
        string? subjectRef,
        IReadOnlyList<SyntheticMembershipBoundary>? history,
        DateTimeOffset periodStartInclusive,
        DateTimeOffset periodEndExclusive,
        DateTimeOffset observedAt)
    {
        if (!IsSyntheticRef(subjectRef) ||
            periodStartInclusive >= periodEndExclusive ||
            periodEndExclusive > observedAt)
            return MembershipWindowObservation.InvalidInput;

        if (history is null || history.Count == 0)
            return MembershipWindowObservation.EvidenceInsufficient;

        // The caller's "history" has no trusted issuer. Require exact
        // revision, subject, event identity, monotone instant and legal
        // activation/nonrenewal/reactivation alternation. Never sort,
        // deduplicate, fill missing events, or infer an omitted boundary.
        var seen = new HashSet<Guid>();
        for (var i = 0; i < history.Count; i++)
        {
            var marker = history[i];
            if (marker.EventId == Guid.Empty ||
                !seen.Add(marker.EventId) ||
                marker.Revision != i + 1L ||
                !string.Equals(marker.SubjectRef, subjectRef,
                    StringComparison.Ordinal) ||
                marker.EffectiveAt > observedAt)
                return MembershipWindowObservation.InvalidHistory;

            var expected = i == 0
                ? SyntheticMembershipBoundaryKind.ActivationRecorded
                : history[i - 1].Kind == SyntheticMembershipBoundaryKind.NonRenewalRecorded
                    ? SyntheticMembershipBoundaryKind.ReactivationRecorded
                    : SyntheticMembershipBoundaryKind.NonRenewalRecorded;
            if (marker.Kind != expected ||
                (i > 0 && marker.EffectiveAt <= history[i - 1].EffectiveAt))
                return MembershipWindowObservation.InvalidHistory;
        }

        // A closing next event is required to claim even hypothetical
        // complete historical coverage; no current entitlement is inferred.
        if (periodStartInclusive < history[0].EffectiveAt ||
            periodEndExclusive > history[^1].EffectiveAt)
            return MembershipWindowObservation.EvidenceInsufficient;

        for (var i = 0; i < history.Count - 1; i++)
        {
            if (periodStartInclusive >= history[i].EffectiveAt &&
                periodEndExclusive <= history[i + 1].EffectiveAt)
                return history[i].Kind == SyntheticMembershipBoundaryKind.NonRenewalRecorded
                    ? MembershipWindowObservation.WithinClosedRecordedGap
                    : MembershipWindowObservation.WithinClosedRecordedMembership;
        }

        // Query intersects two or more closed segments; never split and
        // blindly credit one side, and never decide financial eligibility.
        return MembershipWindowObservation.CrossesRecordedBoundary;
    }

    private static bool IsSyntheticRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
