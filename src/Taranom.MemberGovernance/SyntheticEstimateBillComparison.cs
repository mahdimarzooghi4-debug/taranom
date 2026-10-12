using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// TRN-BIZ-050/051/052: compare only two UNVERIFIED synthetic documents,
// never decide overcharging, outstanding debt, coverage, refund or payment.
internal enum SyntheticEstimateBillComparisonState
{
    InvalidSource = 1,
    MemberScopeDenied = 2,
    ClaimLinkMissing = 3,
    PriorChoiceHistoryInvalid = 4,
    UnverifiedClaimsOnly = 5,
    UnverifiedClaimDifferences = 6,
    IncompleteOrDisputedSources = 7
}

internal enum SyntheticEstimateBillLineState
{
    UnverifiedSameAmount = 1,
    UnverifiedDifferentAmount = 2,
    PreviewSourceMissing = 3,
    PreviewSourceConflicted = 4,
    BillSourceMissingOrWithdrawn = 5,
    BillSourceDisputed = 6,
    UnverifiedCurrencyDifference = 7
}

internal readonly record struct SyntheticEstimateBillLineRead(
    SyntheticCostComponent Component, SyntheticEstimateBillLineState State);

internal readonly record struct SyntheticEstimateBillComparisonRead(
    SyntheticEstimateBillComparisonState State,
    string? MemberRef,
    string? UnverifiedTreatmentRef,
    Guid PreviewId,
    long PreviewRevision,
    Guid BillPacketId,
    long BillViewRevision,
    CostAwarenessChoice? PreviousUnverifiedChoice,
    ImmutableArray<SyntheticEstimateBillLineRead> Lines,
    bool OfficialBillConfirmed,
    bool PaymentOrRefundAuthorized,
    bool WalletMutated,
    bool ClinicalConsentGranted);

// A claimed linkage between preview and treatment is NOT verified: the
// preview contract has no treatment identifier. Member scope and opaque
// test: relation are structural filters only, never provenance attestation.
internal static class SyntheticEstimateBillComparison
{
    internal static SyntheticEstimateBillComparisonRead Inspect(
        SyntheticCostPreview oldPreview,
        CostAwarenessSnapshot? oldChoiceHistory,
        string? claimedTreatmentRef,
        string? unverifiedCorrespondenceRef,
        Guid billPacketId,
        long billViewRevision,
        IReadOnlyList<SyntheticBillEvidence>? billEvidence)
    {
        static SyntheticEstimateBillComparisonRead Deny(
            SyntheticEstimateBillComparisonState state) =>
            new(state, null, null, Guid.Empty, 0, Guid.Empty, 0, null,
                ImmutableArray<SyntheticEstimateBillLineRead>.Empty,
                false, false, false, false);

        // Reuse the established canonical cost source and choice-history
        // validators, with no parallel choice semantics or ad hoc hash trust.
        var past = SyntheticCostChoiceContinuity.Inspect(
            oldPreview, oldChoiceHistory, oldPreview);
        if (past.State == SyntheticCostChoiceContinuityState.InvalidSource)
            return Deny(SyntheticEstimateBillComparisonState.InvalidSource);
        if (past.State == SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid)
            return Deny(SyntheticEstimateBillComparisonState.PriorChoiceHistoryInvalid);

        if (!SyntheticCostPreviewBuilder.Synthetic(claimedTreatmentRef) ||
            billPacketId == Guid.Empty || billViewRevision <= 0 ||
            billEvidence is null)
            return Deny(SyntheticEstimateBillComparisonState.InvalidSource);
        if (!SyntheticCostPreviewBuilder.Synthetic(unverifiedCorrespondenceRef))
            return Deny(SyntheticEstimateBillComparisonState.ClaimLinkMissing);

        var bill = SyntheticTreatmentBillingAssembler.Build(
            oldPreview.MemberRef, claimedTreatmentRef, billPacketId,
            billViewRevision, billEvidence);
        if (bill.State == SyntheticBillViewState.Invalid ||
            bill.EvidenceFingerprint is not { Length: 64 } || bill.Lines.Length != 5)
            return Deny(SyntheticEstimateBillComparisonState.InvalidSource);

        // The bill assembler checks all its event sources against preview
        // member. A mismatch is not a real cross-member billing disclosure.
        if (billEvidence.Any(e => e.MemberRef != oldPreview.MemberRef))
            return Deny(SyntheticEstimateBillComparisonState.MemberScopeDenied);

        var lines = ImmutableArray.CreateBuilder<SyntheticEstimateBillLineRead>(5);
        foreach (var prior in oldPreview.Lines)
        {
            var lane = Map(prior.Component);
            var latest = bill.Lines.Single(e => e.Lane == lane);
            var state = prior.State switch
            {
                SyntheticCostEvidenceState.Missing =>
                    SyntheticEstimateBillLineState.PreviewSourceMissing,
                SyntheticCostEvidenceState.ConflictingClaims =>
                    SyntheticEstimateBillLineState.PreviewSourceConflicted,
                _ => latest.LastChange switch
                {
                    null or SyntheticBillEvidenceChange.Withdrawn =>
                        SyntheticEstimateBillLineState.BillSourceMissingOrWithdrawn,
                    SyntheticBillEvidenceChange.Disputed =>
                        SyntheticEstimateBillLineState.BillSourceDisputed,
                    _ when prior.CurrencyCode != latest.CurrencyCode =>
                        SyntheticEstimateBillLineState.UnverifiedCurrencyDifference,
                    _ when prior.ClaimedAmount == latest.UnverifiedAmount =>
                        SyntheticEstimateBillLineState.UnverifiedSameAmount,
                    _ => SyntheticEstimateBillLineState.UnverifiedDifferentAmount
                }
            };
            lines.Add(new(prior.Component, state));
        }

        var result = lines.ToImmutable();
        var allComparable = result.All(x => x.State is
            SyntheticEstimateBillLineState.UnverifiedSameAmount or
            SyntheticEstimateBillLineState.UnverifiedDifferentAmount);
        var stateResult = !allComparable
            ? SyntheticEstimateBillComparisonState.IncompleteOrDisputedSources
            : result.Any(x => x.State == SyntheticEstimateBillLineState.UnverifiedDifferentAmount)
                ? SyntheticEstimateBillComparisonState.UnverifiedClaimDifferences
                : SyntheticEstimateBillComparisonState.UnverifiedClaimsOnly;

        return new(stateResult, oldPreview.MemberRef, claimedTreatmentRef,
            oldPreview.PreviewId, oldPreview.PreviewRevision, billPacketId,
            bill.ViewRevision, past.PreviousUnverifiedChoice, result,
            false, false, false, false);
    }

    private static SyntheticBillLane Map(SyntheticCostComponent component) =>
        component switch
        {
            SyntheticCostComponent.ProviderService => SyntheticBillLane.ProviderService,
            SyntheticCostComponent.PrimaryInsurance => SyntheticBillLane.PrimaryInsurance,
            SyntheticCostComponent.SupplementalInsurance => SyntheticBillLane.SupplementalInsurance,
            SyntheticCostComponent.TreatmentCredit => SyntheticBillLane.TreatmentCredit,
            SyntheticCostComponent.MemberRemainder => SyntheticBillLane.MemberRemainder,
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
}
