using Taranom.CenterAuthorityBoundary;

namespace Taranom.CenterDataAccessInterlock;

// Synthetic-only, internal application boundary: NOT an HTTP or Production API.
// This intentionally cannot consume real protected data while the authoritative
// identity/center/delegation and domain read/write contracts remain unapproved.
internal enum CenterOperationDisposition
{
    InvalidRequest = 1,
    NotConnected = 2,
    Denied = 3
}

internal readonly record struct CenterOperationRejection(
    CenterOperationDisposition Disposition,
    CenterAuthorityResult EvidenceReason);

// A seam for a future SERVER-OWNED private data adapter, intentionally
// non-operational here. No implementer lives in any runtime project.
internal interface ICenterSensitiveDataPort
{
    Task ReadAsync(CenterAuthorityRequest request, CancellationToken cancellationToken);
    Task WriteAsync(CenterAuthorityRequest request, CancellationToken cancellationToken);
}

internal sealed class CenterDataAdmissionInterlock
{
    private readonly CenterAuthorityGate _gate;
    private readonly ICenterSensitiveDataPort _sensitivePort;

    // A data port is injected in TESTS to detect accidental data access.
    // Its presence NEVER establishes identity, actor-to-center ownership,
    // clinical authorization or approval to operate.
    internal CenterDataAdmissionInterlock(
        CenterAuthorityGate gate, ICenterSensitiveDataPort sensitivePort)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _sensitivePort = sensitivePort ?? throw new ArgumentNullException(nameof(sensitivePort));
    }

    internal async Task<CenterOperationRejection> HandleAsync(
        CenterAuthorityRequest request, CancellationToken ct = default)
    {
        // Preserves the precise reason for internal negative tests, but never
        // surfaces server identifiers, synthetic records or provider errors.
        var check = await _gate.AssessAsync(request, ct);

        // The gate only measures preconditions, including for positive
        // synthetic evidence. It is NEVER an authorization proof.
        // Do not call _sensitivePort.ReadAsync or WriteAsync here. An approved
        // external attestation and domain-owned grant must be separately built
        // and reviewed BEFORE a data path can exist in a future bounded context.
        if (check == CenterAuthorityResult.InvalidRequest)
            return new(CenterOperationDisposition.InvalidRequest, check);
        if (check is CenterAuthorityResult.AuthoritativeSourceUnavailable
            or CenterAuthorityResult.PreconditionsPresentOnly)
            return new(CenterOperationDisposition.NotConnected, check);
        return new(CenterOperationDisposition.Denied, check);
    }

    // This explicit property asserts that there is NO route to the protected
    // port in this product slice, even if a test double injects one.
    internal bool HasOperationalDataPath => false;
}
