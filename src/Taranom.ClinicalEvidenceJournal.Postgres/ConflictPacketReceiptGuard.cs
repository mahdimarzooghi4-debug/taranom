using Taranom.ClinicalEvidenceIntake;
using IntakeDomain = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// The alleged recipient is an untrusted synthetic identity, NEVER an actual
// legal reviewer or person receiving a real health/conflict file.
internal enum TechnicalReceiptOutcome
{
    Invalid = 1,
    AuthoritySourceUnavailable = 2,
    RecipientUnverified = 3,
    PacketScopeMismatch = 4,
    RecipientIsSubjectOrInitiator = 5,
    PacketMissingOrCorrupt = 6,
    SupersededSourceRevision = 7,
    SimulatedPrerequisitesOnly = 8,
    SyntheticTechnicalReceiptRecorded = 9,
    ExactReplay = 10,
    ConflictingReplay = 11,
    AlreadyNotedForPacket = 12,
    SimulatedIssuerSnapshotChanged = 13,
    HistoricalIssuerVersionDifferentOnReplay = 14
}

internal readonly record struct TechnicalReceiptCommand(
    Guid RequestId, Guid PacketId, string CenterRef,
    string RecipientClaimRef, string GrantVersionRef, string ReceiptEvidenceRef);

internal readonly record struct TechnicalReceiptSourceEvidence(
    Guid PacketId, Guid RequestId, string CenterRef, string SessionSubjectRef,
    string ResolvedRecipientRef, string CurrentGrantVersionRef,
    string ReceiptEvidenceRef, string SimulatedIssuerRef,
    long SimulatedIssuerRevision,
    bool SimulatedSessionVerified, bool SimulatedAssignmentCurrent,
    bool SimulatedGrantNotRevoked, bool SimulatedAuditAvailable);

// An interface seam for future legally approved issuer/assignment registry.
// Only TEST DOUBLES may implement it in this Sprint. No actual source exists.
internal interface ITechnicalReceiptAuthoritySource
{
    Task<TechnicalReceiptSourceEvidence?> ResolveAsync(
        Guid packetId, Guid requestId, CancellationToken cancellationToken);
}

internal readonly record struct TechnicalReceiptSourceCheck(
    TechnicalReceiptOutcome Outcome, TechnicalReceiptSourceEvidence? Evidence);

// The source revision and issuer below are ONLY simulated lineage markers.
// They do not supply real authority or an atomic revocation guarantee.
internal static class ConflictPacketReceiptGuard
{
    internal static bool Valid(TechnicalReceiptCommand x) =>
        x.RequestId != Guid.Empty && x.PacketId != Guid.Empty &&
        IntakeDomain.IsSyntheticRef(x.CenterRef) &&
        IntakeDomain.IsSyntheticRef(x.RecipientClaimRef) &&
        IntakeDomain.IsSyntheticRef(x.GrantVersionRef) &&
        IntakeDomain.IsSyntheticRef(x.ReceiptEvidenceRef);

    internal static async Task<TechnicalReceiptOutcome> CheckAsync(
        TechnicalReceiptCommand command,
        ITechnicalReceiptAuthoritySource? source,
        CancellationToken ct = default) =>
        (await InspectAsync(command, source, ct)).Outcome;

    internal static async Task<TechnicalReceiptSourceCheck> InspectAsync(
        TechnicalReceiptCommand command,
        ITechnicalReceiptAuthoritySource? source,
        CancellationToken ct = default)
    {
        if (!Valid(command)) return new(TechnicalReceiptOutcome.Invalid, null);
        // No source => stop BEFORE reading even synthetic conflict packets.
        if (source is null) return new(TechnicalReceiptOutcome.AuthoritySourceUnavailable, null);
        TechnicalReceiptSourceEvidence? candidate;
        try { candidate = await source.ResolveAsync(command.PacketId, command.RequestId, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return new(TechnicalReceiptOutcome.AuthoritySourceUnavailable, null); }
        if (candidate is not { } x)
            return new(TechnicalReceiptOutcome.AuthoritySourceUnavailable, null);
        if (x.PacketId != command.PacketId || x.RequestId != command.RequestId ||
            x.CenterRef != command.CenterRef)
            return new(TechnicalReceiptOutcome.PacketScopeMismatch, null);
        if (!x.SimulatedSessionVerified || !x.SimulatedAssignmentCurrent ||
            !x.SimulatedGrantNotRevoked || !x.SimulatedAuditAvailable ||
            !IntakeDomain.IsSyntheticRef(x.SessionSubjectRef) ||
            !IntakeDomain.IsSyntheticRef(x.ResolvedRecipientRef) ||
            !IntakeDomain.IsSyntheticRef(x.CurrentGrantVersionRef) ||
            !IntakeDomain.IsSyntheticRef(x.ReceiptEvidenceRef) ||
            !IntakeDomain.IsSyntheticRef(x.SimulatedIssuerRef) ||
            x.SimulatedIssuerRevision < 1 ||
            !string.Equals(x.ReceiptEvidenceRef, command.ReceiptEvidenceRef,
                StringComparison.Ordinal) ||
            x.SessionSubjectRef != x.ResolvedRecipientRef ||
            x.ResolvedRecipientRef != command.RecipientClaimRef ||
            x.CurrentGrantVersionRef != command.GrantVersionRef)
            return new(TechnicalReceiptOutcome.RecipientUnverified, null);
        // A positive fake test source is never an authorization to receive.
        return new(TechnicalReceiptOutcome.SimulatedPrerequisitesOnly, x);
    }
}
