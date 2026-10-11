using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Taranom.MemberGovernance;

// TRN-BIZ-012/013/014/015/016: information for a human FINANCIAL review,
// never an entitlement determination, balance, ledger entry, or repayment.
// Only untrusted test: markers. Existing coverage & waiting reducers are reused.
internal readonly record struct SyntheticCreditPeriodClaim(
    Guid ClaimId,
    string? MemberRef,
    string? UnverifiedIncomeSourceRef,
    long SourceRevision,
    DateTimeOffset PeriodStartInclusive,
    DateTimeOffset PeriodEndExclusive,
    DateTimeOffset ClaimRecordedAt,
    EvidenceCheck ClaimedIndependentEvidenceReview);

internal enum SyntheticCreditClaimObservation
{
    PrematurePeriodClaim = 1,
    HistoricalMembershipReviewMissing = 2,
    HistoricalMembershipReviewRejected = 3,
    HistoricalMembershipEvidencePresentOnly = 4,
    ClaimedNonMembershipPeriod = 5,
    CrossesMembershipBoundary = 6,
    TimelineEvidenceInsufficient = 7
}

internal enum SyntheticCreditPortfolioState
{
    Invalid = 1,
    HistoryInvalid = 2,
    EvidenceInsufficient = 3,
    UnverifiedClaimsOnly = 4
}

internal readonly record struct SyntheticCreditClaimRead(
    Guid ClaimId,
    MembershipWindowObservation WindowObservation,
    SyntheticCreditClaimObservation Observation,
    bool RecordedAfterAttributionPeriod);

// This snapshot contains no amount, source identity proof or posting flag.
internal readonly record struct SyntheticCreditPortfolioRead(
    SyntheticCreditPortfolioState State,
    string? MemberRef,
    WaitingPeriodObservation? HypotheticalInitialWait,
    ImmutableArray<SyntheticCreditClaimRead> Claims,
    string? Fingerprint);

// No mutation or hidden rules. Financial entitlement and document review
// require independently authenticated authorities that DO NOT exist here.
internal static class SyntheticCreditPeriodReviewProjector
{
    internal static SyntheticCreditPortfolioRead Build(
        string? memberRef,
        IReadOnlyList<SyntheticMembershipBoundary>? recordedTimeline,
        IReadOnlyList<SyntheticCreditPeriodClaim>? proposedClaims,
        DateTimeOffset observedAt)
    {
        var invalid = new SyntheticCreditPortfolioRead(
            SyntheticCreditPortfolioState.Invalid, null, null,
            ImmutableArray<SyntheticCreditClaimRead>.Empty, null);

        if (!SyntheticCostPreviewBuilder.Synthetic(memberRef) ||
            recordedTimeline is null || proposedClaims is null)
            return invalid;

        var ids = new HashSet<Guid>();
        var sourcePeriodKeys = new HashSet<(string?, long, DateTimeOffset, DateTimeOffset)>();
        foreach (var claim in proposedClaims)
        {
            if (claim.ClaimId == Guid.Empty || !ids.Add(claim.ClaimId) ||
                claim.MemberRef != memberRef ||
                !SyntheticCostPreviewBuilder.Synthetic(claim.UnverifiedIncomeSourceRef) ||
                claim.SourceRevision <= 0 ||
                claim.PeriodStartInclusive >= claim.PeriodEndExclusive ||
                claim.PeriodEndExclusive > observedAt ||
                claim.ClaimRecordedAt > observedAt ||
                !Enum.IsDefined(claim.ClaimedIndependentEvidenceReview) ||
                !sourcePeriodKeys.Add((claim.UnverifiedIncomeSourceRef, claim.SourceRevision,
                    claim.PeriodStartInclusive, claim.PeriodEndExclusive)))
                return invalid;
        }

        // Need a complete CLOSED interval for structural history validation.
        // One activation marker alone is deliberately not a proven current
        // membership, and this model does NOT infer a live activation.
        if (recordedTimeline.Count < 2)
            return new(SyntheticCreditPortfolioState.EvidenceInsufficient,
                memberRef, null, ImmutableArray<SyntheticCreditClaimRead>.Empty, null);

        var anchor = SyntheticMembershipCoverageWindow.Observe(memberRef,
            recordedTimeline, recordedTimeline[0].EffectiveAt,
            recordedTimeline[1].EffectiveAt, observedAt);
        if (anchor == MembershipWindowObservation.InvalidHistory ||
            anchor == MembershipWindowObservation.InvalidInput)
            return new(SyntheticCreditPortfolioState.HistoryInvalid,
                null, null, ImmutableArray<SyntheticCreditClaimRead>.Empty, null);
        if (anchor != MembershipWindowObservation.WithinClosedRecordedMembership)
            return new(SyntheticCreditPortfolioState.EvidenceInsufficient,
                memberRef, null, ImmutableArray<SyntheticCreditClaimRead>.Empty, null);

        var initialWait = MembershipWaitingPeriod.Observe(
            recordedTimeline[0].EffectiveAt, observedAt);
        var reads = ImmutableArray.CreateBuilder<SyntheticCreditClaimRead>(proposedClaims.Count);
        foreach (var claim in proposedClaims.OrderBy(x => x.ClaimId))
        {
            var window = SyntheticMembershipCoverageWindow.Observe(
                memberRef, recordedTimeline, claim.PeriodStartInclusive,
                claim.PeriodEndExclusive, observedAt);
            var observation =
                claim.ClaimRecordedAt < claim.PeriodEndExclusive
                    ? SyntheticCreditClaimObservation.PrematurePeriodClaim
                : window == MembershipWindowObservation.WithinClosedRecordedGap
                    ? SyntheticCreditClaimObservation.ClaimedNonMembershipPeriod
                : window == MembershipWindowObservation.CrossesRecordedBoundary
                    ? SyntheticCreditClaimObservation.CrossesMembershipBoundary
                : window != MembershipWindowObservation.WithinClosedRecordedMembership
                    ? SyntheticCreditClaimObservation.TimelineEvidenceInsufficient
                : claim.ClaimedIndependentEvidenceReview == EvidenceCheck.Rejected
                    ? SyntheticCreditClaimObservation.HistoricalMembershipReviewRejected
                : claim.ClaimedIndependentEvidenceReview != EvidenceCheck.Confirmed
                    ? SyntheticCreditClaimObservation.HistoricalMembershipReviewMissing
                    : SyntheticCreditClaimObservation.HistoricalMembershipEvidencePresentOnly;
            reads.Add(new(claim.ClaimId, window, observation,
                claim.ClaimRecordedAt > claim.PeriodEndExclusive));
        }

        // Order-invariant checksum for synthetic regression only; NEVER a
        // signature/attestation. Capture every source/history field, including
        // the review claim, not only the rendered read projection.
        var data = new StringBuilder("taranom.credit-period-review.synthetic.v1");
        void Add(string value) => data.Append('|')
            .Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        Add(memberRef!);
        Add(observedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        foreach (var marker in recordedTimeline)
        {
            Add(marker.Revision.ToString(CultureInfo.InvariantCulture));
            Add(marker.EventId.ToString("N"));
            Add(marker.SubjectRef);
            Add(((int)marker.Kind).ToString(CultureInfo.InvariantCulture));
            Add(marker.EffectiveAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }
        foreach (var claim in proposedClaims.OrderBy(x => x.ClaimId))
        {
            Add(claim.ClaimId.ToString("N"));
            Add(claim.MemberRef!);
            Add(claim.UnverifiedIncomeSourceRef!);
            Add(claim.SourceRevision.ToString(CultureInfo.InvariantCulture));
            Add(claim.PeriodStartInclusive.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Add(claim.PeriodEndExclusive.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Add(claim.ClaimRecordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Add(((int)claim.ClaimedIndependentEvidenceReview).ToString(CultureInfo.InvariantCulture));
        }

        return new(SyntheticCreditPortfolioState.UnverifiedClaimsOnly,
            memberRef, initialWait, reads.ToImmutable(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToString()))));
    }
}
