namespace Taranom.MemberGovernance;

// Internal-only contract exercise for SELF-SERVICE MEMBER READS. This is not a
// live authentication or authorization service, wire contract or grant token.
// Provider and administrator workflows require separately governed boundaries.
internal enum SelfReadActorKind
{
    Unknown = 0,
    Member = 1,
    Sponsor = 2,
    Employer = 3,
    Provider = 4,
    Administrator = 5
}

internal enum SelfReadBoundaryResult
{
    EvidenceMissing = 0,
    ActorDenied = 1,
    ScopeDenied = 2,
    PreconditionsRejected = 3,
    PreconditionsPresentOnly = 4
}

// All values are *claimed inputs* until a future .NET-owned server establishes
// their actual provenance. The refs are deliberately synthetic-only and the
// caller must never promote this assessment to a data authorization decision.
internal readonly record struct SelfReadBoundaryInput(
    SelfReadActorKind ActorKind,
    string? ServerResolvedMemberRef,
    string? ServerResolvedTenantRef,
    string? ResourceOwnerMemberRef,
    string? ResourceOwnerTenantRef,
    string? BrowserMemberHint,
    string? BrowserTenantHint,
    EvidenceCheck CurrentSessionNotRevoked,
    EvidenceCheck CurrentIdentityMemberLink,
    EvidenceCheck CurrentPermissionVersion,
    ReadPrerequisiteSnapshot OtherReadPrerequisites);

internal static class MemberSelfReadBoundary
{
    internal static SelfReadBoundaryResult Assess(SelfReadBoundaryInput input)
    {
        // Sponsor/employer relationships NEVER imply permission to read an
        // individual's private self-service member projection. This rule is
        // intentionally narrower than future clinician/admin workflows.
        if (input.ActorKind != SelfReadActorKind.Member)
            return SelfReadBoundaryResult.ActorDenied;

        if (!IsSyntheticRef(input.ServerResolvedMemberRef) ||
            !IsSyntheticRef(input.ServerResolvedTenantRef) ||
            !IsSyntheticRef(input.ResourceOwnerMemberRef) ||
            !IsSyntheticRef(input.ResourceOwnerTenantRef))
            return SelfReadBoundaryResult.EvidenceMissing;

        if (!string.Equals(input.ServerResolvedMemberRef,
                input.ResourceOwnerMemberRef, StringComparison.Ordinal) ||
            !string.Equals(input.ServerResolvedTenantRef,
                input.ResourceOwnerTenantRef, StringComparison.Ordinal))
            return SelfReadBoundaryResult.ScopeDenied;

        // Browser parameters are never an identity source. Even a request that
        // supplies all the other evidence must not override the server scope.
        if ((input.BrowserMemberHint is not null &&
                (!IsSyntheticRef(input.BrowserMemberHint) ||
                 !string.Equals(input.BrowserMemberHint,
                     input.ServerResolvedMemberRef, StringComparison.Ordinal))) ||
            (input.BrowserTenantHint is not null &&
                (!IsSyntheticRef(input.BrowserTenantHint) ||
                 !string.Equals(input.BrowserTenantHint,
                     input.ServerResolvedTenantRef, StringComparison.Ordinal))))
            return SelfReadBoundaryResult.ScopeDenied;

        // The actual authorities for these checks do NOT exist in Sprint 03.
        // Confirmation supplied by a caller is not cryptographic attestation.
        var statuses = new[]
        {
            input.CurrentSessionNotRevoked,
            input.CurrentIdentityMemberLink,
            input.CurrentPermissionVersion
        };
        if (statuses.Any(x => x == EvidenceCheck.Rejected))
            return SelfReadBoundaryResult.PreconditionsRejected;
        if (statuses.Any(x => x != EvidenceCheck.Confirmed))
            return SelfReadBoundaryResult.EvidenceMissing;

        return ReadPrerequisiteGate.Evaluate(input.OtherReadPrerequisites) switch
        {
            ReadPrerequisiteResult.Rejected =>
                SelfReadBoundaryResult.PreconditionsRejected,
            ReadPrerequisiteResult.PreconditionsPresent =>
                SelfReadBoundaryResult.PreconditionsPresentOnly,
            _ => SelfReadBoundaryResult.EvidenceMissing
        };
    }

    private static bool IsSyntheticRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
