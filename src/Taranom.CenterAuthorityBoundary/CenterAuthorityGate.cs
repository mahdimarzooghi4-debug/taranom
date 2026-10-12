namespace Taranom.CenterAuthorityBoundary;

// Internal TEST-ONLY contract. No HTTP, OIDC/JWT, trusted IdP,
// center manager registry or clinical authorization implementation exists.
internal enum CenterReadWriteOperation
{
    ReadDepartments = 1,
    ReadAffiliations = 2,
    ReadCenterAudit = 3,
    DefineDepartment = 4,
    RecordDoctorAffiliation = 5,
    AssignDoctorToDepartment = 6,
    EndDoctorAffiliation = 7
}

internal enum CenterActorCategory
{
    Unknown = 0,
    CenterRepresentative = 1,
    Doctor = 2,
    Sponsor = 3,
    Employer = 4,
    Administrator = 5
}

internal enum CenterEvidenceCheck
{
    Missing = 0,
    Confirmed = 1,
    Rejected = 2
}

internal enum CenterAuthorityResult
{
    InvalidRequest = 1,
    AuthoritativeSourceUnavailable = 2,
    IdentityNotVerified = 3,
    CenterScopeDenied = 4,
    ManagerAuthorityDenied = 5,
    OperationNotPermitted = 6,
    PermissionStaleOrRevoked = 7,
    AuditUnavailable = 8,
    PreconditionsPresentOnly = 9
}

// Everything browser-supplied is an untrusted hint, including role.
// No request field can assign the actual operator/center/tenant.
internal readonly record struct CenterAuthorityRequest(
    Guid AttemptRef,
    CenterReadWriteOperation Operation,
    string? RequestedCenterRef,
    string? BrowserActorHint,
    string? BrowserCenterHint,
    string? BrowserTenantHint,
    string? BrowserRoleHint);

// A future independently trusted server adapter must resolve ALL properties
// from an authenticated session + current domain-owned center/role registry.
// A test double can populate this object; it is not production attestation.
internal readonly record struct CenterServerEvidence(
    string? SessionSubjectRef,
    string? ResolvedActorRef,
    string? DelegatedManagerRef,
    string? ResolvedCenterRef,
    string? ResourceCenterRef,
    string? SessionTenantRef,
    string? CenterTenantRef,
    string? ResourceTenantRef,
    string? PreviousPermissionVersionRef,
    string? CurrentPermissionVersionRef,
    CenterActorCategory ActorCategory,
    CenterReadWriteOperation AllowedOperation,
    CenterEvidenceCheck IssuerVerified,
    CenterEvidenceCheck SessionCurrent,
    CenterEvidenceCheck IdentityMappingCurrent,
    CenterEvidenceCheck ManagerAssignmentCurrent,
    CenterEvidenceCheck GrantNotRevoked,
    CenterEvidenceCheck OperationScopeValid,
    CenterEvidenceCheck AuditReady);

// The production implementation is deliberately ABSENT. This is a seam,
// not an invented identity provider or permission registry.
internal interface ICenterServerEvidenceSource
{
    Task<CenterServerEvidence?> ResolveFromServerAsync(
        Guid attemptRef, CenterReadWriteOperation operation,
        CancellationToken cancellationToken);
}

internal sealed class CenterAuthorityGate(ICenterServerEvidenceSource? source = null)
{
    internal async Task<CenterAuthorityResult> AssessAsync(
        CenterAuthorityRequest request, CancellationToken ct = default)
    {
        if (request.AttemptRef == Guid.Empty ||
            !Enum.IsDefined(request.Operation) ||
            !IsSynthetic(request.RequestedCenterRef))
            return CenterAuthorityResult.InvalidRequest;

        // Disconnected mode is the normal/default mode: fail closed,
        // independent of browser role=admin, confirmed=true, center=... flags.
        if (source is null)
            return CenterAuthorityResult.AuthoritativeSourceUnavailable;

        CenterServerEvidence? evidence;
        try
        {
            evidence = await source.ResolveFromServerAsync(
                request.AttemptRef, request.Operation, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Do not expose token/provider/internal error details.
            return CenterAuthorityResult.AuthoritativeSourceUnavailable;
        }

        if (evidence is null)
            return CenterAuthorityResult.AuthoritativeSourceUnavailable;

        var e = evidence.Value;

        // Test-only names and exact, case-sensitive scope protect against
        // accidental acceptance of raw national IDs or arbitrary URLs.
        if (!IsSynthetic(e.SessionSubjectRef) ||
            !IsSynthetic(e.ResolvedActorRef) ||
            !IsSynthetic(e.DelegatedManagerRef) ||
            !IsSynthetic(e.ResolvedCenterRef) ||
            !IsSynthetic(e.ResourceCenterRef) ||
            !IsSynthetic(e.SessionTenantRef) ||
            !IsSynthetic(e.CenterTenantRef) ||
            !IsSynthetic(e.ResourceTenantRef) ||
            !IsSynthetic(e.PreviousPermissionVersionRef) ||
            !IsSynthetic(e.CurrentPermissionVersionRef) ||
            e.IssuerVerified != CenterEvidenceCheck.Confirmed ||
            e.SessionCurrent != CenterEvidenceCheck.Confirmed ||
            e.IdentityMappingCurrent != CenterEvidenceCheck.Confirmed ||
            !string.Equals(e.SessionSubjectRef, e.ResolvedActorRef, StringComparison.Ordinal))
            return CenterAuthorityResult.IdentityNotVerified;

        if (e.ActorCategory != CenterActorCategory.CenterRepresentative ||
            !string.Equals(e.DelegatedManagerRef, e.ResolvedActorRef,
                StringComparison.Ordinal))
            return CenterAuthorityResult.ManagerAuthorityDenied;

        if (!string.Equals(request.RequestedCenterRef,
                e.ResolvedCenterRef, StringComparison.Ordinal) ||
            !string.Equals(e.ResolvedCenterRef,
                e.ResourceCenterRef, StringComparison.Ordinal) ||
            !string.Equals(e.SessionTenantRef,
                e.CenterTenantRef, StringComparison.Ordinal) ||
            !string.Equals(e.CenterTenantRef,
                e.ResourceTenantRef, StringComparison.Ordinal))
            return CenterAuthorityResult.CenterScopeDenied;

        // Browser hints can at most corroborate server-owned identity. Neither
        // role hints nor seemingly matching center/actor can create a grant.
        if ((request.BrowserActorHint is not null &&
                !string.Equals(request.BrowserActorHint,
                    e.ResolvedActorRef, StringComparison.Ordinal)) ||
            (request.BrowserCenterHint is not null &&
                !string.Equals(request.BrowserCenterHint,
                    e.ResolvedCenterRef, StringComparison.Ordinal)) ||
            (request.BrowserTenantHint is not null &&
                !string.Equals(request.BrowserTenantHint,
                    e.CenterTenantRef, StringComparison.Ordinal)))
            return CenterAuthorityResult.CenterScopeDenied;

        if (e.ManagerAssignmentCurrent != CenterEvidenceCheck.Confirmed)
            return CenterAuthorityResult.ManagerAuthorityDenied;

        if (e.GrantNotRevoked != CenterEvidenceCheck.Confirmed ||
            !string.Equals(e.PreviousPermissionVersionRef,
                e.CurrentPermissionVersionRef, StringComparison.Ordinal))
            return CenterAuthorityResult.PermissionStaleOrRevoked;

        // Authorization is specific to the requested operation: membership
        // in one center cannot imply all rights or an adjacent care/PHI grant.
        if (e.AllowedOperation != request.Operation ||
            e.OperationScopeValid != CenterEvidenceCheck.Confirmed)
            return CenterAuthorityResult.OperationNotPermitted;

        if (e.AuditReady != CenterEvidenceCheck.Confirmed)
            return CenterAuthorityResult.AuditUnavailable;

        // IMPORTANT: This is NOT "AUTHORIZED", NOT a token and NOT an
        // instruction to call PgClinicalCenterJournal.ApplyAsync/ReadAsync.
        // EvidenceSource is test-supplied until a real issuer is approved.
        return CenterAuthorityResult.PreconditionsPresentOnly;
    }

    private static bool IsSynthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
}
