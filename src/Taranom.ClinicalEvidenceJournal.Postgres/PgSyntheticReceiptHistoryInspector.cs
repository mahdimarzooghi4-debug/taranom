namespace Taranom.ClinicalEvidenceJournal.Postgres;

// This is only a historical matching check over synthetic test claims.
// Even "matches" NEVER means that a person was authorized, received a file,
// or that the issuer's current revocation state is authoritative.
internal enum SyntheticReceiptHistoryStatus
{
    InvalidQuery = 1,
    SourceUnavailable = 2,
    RecipientOrGrantUnverified = 3,
    NoHistoricalReceipt = 4,
    HistoricalReceiptCorrupt = 5,
    RecordedCommandMismatch = 6,
    HistoricalIssuerVersionDifferent = 7,
    SimulatedVersionMatchesHistoryOnly = 8
}

internal sealed class PgSyntheticReceiptHistoryInspector(
    PgSyntheticPacketReceiptJournal receipts,
    ITechnicalReceiptAuthoritySource? source = null)
{
    internal async Task<SyntheticReceiptHistoryStatus> InspectAsync(
        TechnicalReceiptCommand expected, CancellationToken ct = default)
    {
        if (!ConflictPacketReceiptGuard.Valid(expected))
            return SyntheticReceiptHistoryStatus.InvalidQuery;

        // Recheck an issuer TEST DOUBLE before any historical SQL read. This
        // is deliberately not an API permission check or real authorization.
        var check = await ConflictPacketReceiptGuard.InspectAsync(
            expected, source, ct);
        if (check.Outcome == TechnicalReceiptOutcome.AuthoritySourceUnavailable)
            return SyntheticReceiptHistoryStatus.SourceUnavailable;
        if (check.Outcome != TechnicalReceiptOutcome.SimulatedPrerequisitesOnly ||
            check.Evidence is not { } attested)
            return SyntheticReceiptHistoryStatus.RecipientOrGrantUnverified;

        SyntheticPacketReceipt? stored;
        try
        {
            stored = await receipts.ReadAsync(expected.PacketId, ct);
        }
        catch (InvalidOperationException)
        {
            return SyntheticReceiptHistoryStatus.HistoricalReceiptCorrupt;
        }
        if (stored is null)
            return SyntheticReceiptHistoryStatus.NoHistoricalReceipt;
        if (stored.Command != expected)
            return SyntheticReceiptHistoryStatus.RecordedCommandMismatch;
        if (stored.SimulatedIssuerRef != attested.SimulatedIssuerRef ||
            stored.SimulatedIssuerRevision != attested.SimulatedIssuerRevision)
            return SyntheticReceiptHistoryStatus.HistoricalIssuerVersionDifferent;

        return SyntheticReceiptHistoryStatus.SimulatedVersionMatchesHistoryOnly;
    }
}
