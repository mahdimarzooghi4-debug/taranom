using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// TRN-BIZ-004/005/024/025/026/027: six accepted revenue origins only.
// INTERNAL, READ-ONLY and SYNTHETIC-ONLY; no allocation formula, sums,
// recognized revenue, wallet posting, fund profit or economic promise.
internal enum SyntheticRevenueStream
{
    TreatmentDevelopmentFundProfitShare = 1,
    HealthDevelopmentFundProfitShare = 2,
    FixedIncomeFundManagementFeeCashback = 3,
    SupplementalInsuranceReferralCommission = 4,
    HospitalInvoiceCommission = 5,
    DirectTaranomIncome = 6
}

internal enum SyntheticRevenueComponent
{
    Unspecified = 0,
    MembershipSubscription = 1
}

internal enum SyntheticRevenueLaneStatus
{
    UnrepresentedInSuppliedEvidence = 1,
    UnverifiedClaimsOnly = 2,
    ClaimedEvidenceMissing = 3,
    ClaimedEvidenceRejected = 4,
    PotentialDuplicateSourceUnresolved = 5
}

internal enum SyntheticRevenueMapStatus
{
    InvalidInput = 1,
    PotentialDoubleCountUnresolved = 2,
    ClaimedEvidenceRejected = 3,
    ClaimedEvidenceMissing = 4,
    UnrepresentedSourcesInSuppliedEvidence = 5,
    UnverifiedSixSourceClaimsOnly = 6
}

// No amount, balance, member allocation, percentages, recognition dates,
// financial benefit or settlement recipient may be inserted in this model.
internal readonly record struct SyntheticRevenueOriginClaim(
    Guid ClaimId,
    string? TenantRef,
    string? OperatingScopeRef,
    string? ClaimedFinancialPeriodRef,
    SyntheticRevenueStream Stream,
    SyntheticRevenueComponent Component,
    string? ClaimedUnderlyingRevenueEventRef,
    long UnderlyingEventRevision,
    string? UnverifiedEvidenceRef,
    EvidenceCheck ClaimedLawfulSource,
    EvidenceCheck ClaimedAccountingClassification,
    EvidenceCheck ClaimedFinancialCustodianAuthority,
    EvidenceCheck ClaimedIndependentAntiDoubleCountReview);

internal readonly record struct SyntheticRevenueLaneRead(
    SyntheticRevenueStream Stream,
    SyntheticRevenueLaneStatus Status,
    int UnverifiedClaimCount,
    int MembershipSubscriptionComponentClaimCount);

internal readonly record struct SyntheticRevenueEvidenceRead(
    SyntheticRevenueMapStatus State,
    ImmutableArray<SyntheticRevenueLaneRead> Sources,
    bool FinancialRevenueRecognized,
    bool AllocationAmountCalculated,
    bool EqualShareDistributionExecuted,
    bool TreatmentWalletCredited,
    bool MembershipFeesBecameSeventhRevenueStream);

// This reducer checks the structural SOURCE OF AN UNTRUSTED CLAIM only.
// It never determines the effective revenue pool, percent, actual member
// eligibility, accounting date, underlying double-count or legal permission.
internal static class SyntheticSixRevenueEvidenceMap
{
    internal static SyntheticRevenueEvidenceRead Inspect(
        string? tenantRef,
        string? operatingScopeRef,
        string? claimedFinancialPeriodRef,
        IReadOnlyList<SyntheticRevenueOriginClaim>? claims)
    {
        static SyntheticRevenueEvidenceRead Deny() =>
            new(SyntheticRevenueMapStatus.InvalidInput,
                ImmutableArray<SyntheticRevenueLaneRead>.Empty,
                false, false, false, false, false);

        if (!Test(tenantRef) || !Test(operatingScopeRef) ||
            !Test(claimedFinancialPeriodRef) || claims is null)
            return Deny();

        var seenClaimIds = new HashSet<Guid>();
        foreach (var claim in claims)
        {
            if (claim.ClaimId == Guid.Empty || !seenClaimIds.Add(claim.ClaimId) ||
                claim.TenantRef != tenantRef ||
                claim.OperatingScopeRef != operatingScopeRef ||
                claim.ClaimedFinancialPeriodRef != claimedFinancialPeriodRef ||
                !Enum.IsDefined(claim.Stream) ||
                !Enum.IsDefined(claim.Component) ||
                (claim.Component == SyntheticRevenueComponent.MembershipSubscription &&
                 claim.Stream != SyntheticRevenueStream.DirectTaranomIncome) ||
                !Test(claim.ClaimedUnderlyingRevenueEventRef) ||
                claim.UnderlyingEventRevision <= 0 ||
                !Test(claim.UnverifiedEvidenceRef) ||
                !Enum.IsDefined(claim.ClaimedLawfulSource) ||
                !Enum.IsDefined(claim.ClaimedAccountingClassification) ||
                !Enum.IsDefined(claim.ClaimedFinancialCustodianAuthority) ||
                !Enum.IsDefined(claim.ClaimedIndependentAntiDoubleCountReview))
                return Deny();
        }

        // The same claimed source EVENT, even at another revision or under
        // another income stream, is not proof of two distinct revenues.
        // Flag for separate reconciliation; do not infer actual duplication.
        var potentiallyDuplicated = claims
            .GroupBy(x => x.ClaimedUnderlyingRevenueEventRef, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .SelectMany(x => x.Select(c => c.ClaimId))
            .ToHashSet();

        var lanes = ImmutableArray.CreateBuilder<SyntheticRevenueLaneRead>(6);
        foreach (var stream in Enum.GetValues<SyntheticRevenueStream>())
        {
            var laneClaims = claims.Where(x => x.Stream == stream).ToArray();
            var status = laneClaims.Length == 0
                ? SyntheticRevenueLaneStatus.UnrepresentedInSuppliedEvidence
                : laneClaims.Any(x => potentiallyDuplicated.Contains(x.ClaimId))
                    ? SyntheticRevenueLaneStatus.PotentialDuplicateSourceUnresolved
                    : laneClaims.Any(x => Evidence(x).Contains(EvidenceCheck.Rejected))
                        ? SyntheticRevenueLaneStatus.ClaimedEvidenceRejected
                        : laneClaims.Any(x => Evidence(x).Any(c => c != EvidenceCheck.Confirmed))
                            ? SyntheticRevenueLaneStatus.ClaimedEvidenceMissing
                            : SyntheticRevenueLaneStatus.UnverifiedClaimsOnly;

            lanes.Add(new(stream, status, laneClaims.Length,
                laneClaims.Count(x => x.Component ==
                    SyntheticRevenueComponent.MembershipSubscription)));
        }

        var read = lanes.ToImmutable();
        var state = read.Any(x =>
            x.Status == SyntheticRevenueLaneStatus.PotentialDuplicateSourceUnresolved)
            ? SyntheticRevenueMapStatus.PotentialDoubleCountUnresolved
            : read.Any(x => x.Status == SyntheticRevenueLaneStatus.ClaimedEvidenceRejected)
                ? SyntheticRevenueMapStatus.ClaimedEvidenceRejected
                : read.Any(x => x.Status == SyntheticRevenueLaneStatus.ClaimedEvidenceMissing)
                    ? SyntheticRevenueMapStatus.ClaimedEvidenceMissing
                    : read.Any(x => x.Status ==
                        SyntheticRevenueLaneStatus.UnrepresentedInSuppliedEvidence)
                        ? SyntheticRevenueMapStatus.UnrepresentedSourcesInSuppliedEvidence
                        : SyntheticRevenueMapStatus.UnverifiedSixSourceClaimsOnly;

        // Claims are never attested actual income nor distribution
        // entitlements; even all six claimed Confirmed outcomes are inert.
        return new(state, read, false, false, false, false, false);
    }

    private static EvidenceCheck[] Evidence(SyntheticRevenueOriginClaim c) =>
    [
        c.ClaimedLawfulSource,
        c.ClaimedAccountingClassification,
        c.ClaimedFinancialCustodianAuthority,
        c.ClaimedIndependentAntiDoubleCountReview
    ];

    private static bool Test(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
