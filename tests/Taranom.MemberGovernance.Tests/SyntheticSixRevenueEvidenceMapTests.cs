using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticSixRevenueEvidenceMapTests
{
    private const string Tenant = "test:tenant-A";
    private const string Scope = "test:operator-A";
    private const string Period = "test:unverified-financial-period-A";

    private static SyntheticRevenueOriginClaim Claim(
        SyntheticRevenueStream stream, int number,
        SyntheticRevenueComponent component = SyntheticRevenueComponent.Unspecified) =>
        new(Guid.NewGuid(), Tenant, Scope, Period, stream, component,
            $"test:source-event-{number}", 1,
            $"test:unverified-evidence-{number}",
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed);

    private static SyntheticRevenueOriginClaim[] Six() =>
        Enum.GetValues<SyntheticRevenueStream>()
            .Select((s, i) => Claim(s, i + 1)).ToArray();

    private static SyntheticRevenueEvidenceRead Map(
        IReadOnlyList<SyntheticRevenueOriginClaim>? claims) =>
        SyntheticSixRevenueEvidenceMap.Inspect(Tenant, Scope, Period, claims);

    [Fact]
    public void SixApprovedRevenueOriginsAreExactAndCannotCreateMoney()
    {
        Assert.Equal(new[] {
            "TreatmentDevelopmentFundProfitShare",
            "HealthDevelopmentFundProfitShare",
            "FixedIncomeFundManagementFeeCashback",
            "SupplementalInsuranceReferralCommission",
            "HospitalInvoiceCommission",
            "DirectTaranomIncome"
        }, Enum.GetNames<SyntheticRevenueStream>());
        var result = Map(Six());
        Assert.Equal(SyntheticRevenueMapStatus.UnverifiedSixSourceClaimsOnly, result.State);
        Assert.Equal(6, result.Sources.Length);
        Assert.All(result.Sources, x => {
            Assert.Equal(SyntheticRevenueLaneStatus.UnverifiedClaimsOnly, x.Status);
            Assert.Equal(1, x.UnverifiedClaimCount);
        });
        Assert.False(result.FinancialRevenueRecognized);
        Assert.False(result.AllocationAmountCalculated);
        Assert.False(result.EqualShareDistributionExecuted);
        Assert.False(result.TreatmentWalletCredited);
        Assert.False(result.MembershipFeesBecameSeventhRevenueStream);
    }

    [Fact]
    public void MemberSubscriptionIsAComponentOfSixthStreamNeverASeventhOne()
    {
        var all = Six();
        all[5] = all[5] with {
            Component = SyntheticRevenueComponent.MembershipSubscription };
        var result = Map(all);
        Assert.Equal(6, result.Sources.Length);
        Assert.Equal(SyntheticRevenueMapStatus.UnverifiedSixSourceClaimsOnly, result.State);
        Assert.Equal(1, result.Sources[5].MembershipSubscriptionComponentClaimCount);
        Assert.Equal(0, result.Sources.Take(5).Sum(x =>
            x.MembershipSubscriptionComponentClaimCount));
        Assert.False(result.TreatmentWalletCredited);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void MembershipFeeCannotBecomeSeparateOrIncorrectIncomeSource(int index)
    {
        var all = Six();
        all[index - 1] = all[index - 1] with {
            Component = SyntheticRevenueComponent.MembershipSubscription };
        Assert.Equal(SyntheticRevenueMapStatus.InvalidInput, Map(all).State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void UnrepresentedSourceIsNotAnAssumedZeroOrApprovedIncome(int removed)
    {
        var claims = Six().Where((_, i) => i != removed).ToArray();
        var result = Map(claims);
        Assert.Equal(SyntheticRevenueMapStatus.UnrepresentedSourcesInSuppliedEvidence,
            result.State);
        Assert.Equal(6, result.Sources.Length);
        Assert.Equal(SyntheticRevenueLaneStatus.UnrepresentedInSuppliedEvidence,
            result.Sources[removed].Status);
        Assert.Equal(0, result.Sources[removed].UnverifiedClaimCount);
        Assert.False(result.AllocationAmountCalculated);
    }

    [Fact]
    public void EmptyFixtureHasSixUnrepresentedSourcesNotRealZeroRevenue()
    {
        var result = Map([]);
        Assert.Equal(SyntheticRevenueMapStatus.UnrepresentedSourcesInSuppliedEvidence,
            result.State);
        Assert.All(result.Sources, x =>
            Assert.Equal(SyntheticRevenueLaneStatus.UnrepresentedInSuppliedEvidence, x.Status));
        Assert.False(result.FinancialRevenueRecognized);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SourceClaimWithRejectedLegalEvidenceNeverBecomesActualIncome(int source)
    {
        var all = Six();
        all[source] = all[source] with {
            ClaimedLawfulSource = EvidenceCheck.Rejected };
        var result = Map(all);
        Assert.Equal(SyntheticRevenueMapStatus.ClaimedEvidenceRejected, result.State);
        Assert.Equal(SyntheticRevenueLaneStatus.ClaimedEvidenceRejected,
            result.Sources[source].Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryIndependentFinancialEvidenceCheckIsMandatory(int field)
    {
        var all = Six();
        all[0] = Check(all[0], field, EvidenceCheck.Missing);
        var result = Map(all);
        Assert.Equal(SyntheticRevenueMapStatus.ClaimedEvidenceMissing, result.State);
        Assert.Equal(SyntheticRevenueLaneStatus.ClaimedEvidenceMissing,
            result.Sources[0].Status);
        Assert.False(result.TreatmentWalletCredited);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExplicitRejectionOverridesMissingOtherChecks(int field)
    {
        var all = Six();
        all[1] = Check(all[1] with {
            ClaimedLawfulSource = EvidenceCheck.Missing }, field, EvidenceCheck.Rejected);
        var result = Map(all);
        Assert.Equal(SyntheticRevenueMapStatus.ClaimedEvidenceRejected, result.State);
    }

    [Theory]
    [InlineData("same-event-across-streams")]
    [InlineData("same-event-other-revision")]
    [InlineData("same-event-same-stream")]
    public void SourceAliasSignalsPotentialDoubleCountingWithoutInventingFraud(string mode)
    {
        var all = Six().ToList();
        var duplicate = all[1] with {
            ClaimId = Guid.NewGuid(),
            ClaimedUnderlyingRevenueEventRef = all[0].ClaimedUnderlyingRevenueEventRef,
            UnderlyingEventRevision = mode == "same-event-other-revision" ? 2 : 1
        };
        if (mode == "same-event-same-stream")
            duplicate = duplicate with { Stream = all[0].Stream };
        all.Add(duplicate);
        var result = Map(all);
        Assert.Equal(SyntheticRevenueMapStatus.PotentialDoubleCountUnresolved, result.State);
        Assert.Equal(SyntheticRevenueLaneStatus.PotentialDuplicateSourceUnresolved,
            result.Sources[0].Status);
        Assert.False(result.EqualShareDistributionExecuted);
    }

    [Fact]
    public void SeparateEvidenceForSameSourceEventCannotHideDuplicateClaim()
    {
        var all = Six();
        all[4] = all[4] with {
            ClaimedUnderlyingRevenueEventRef = all[2].ClaimedUnderlyingRevenueEventRef,
            UnverifiedEvidenceRef = "test:different-document"
        };
        var read = Map(all);
        Assert.Equal(SyntheticRevenueMapStatus.PotentialDoubleCountUnresolved, read.State);
        Assert.Equal(6, read.Sources.Length);
    }

    [Fact]
    public void SameClaimUuidEvenWithDifferentPayloadIsInvalidReadInput()
    {
        var all = Six();
        all[3] = all[3] with { ClaimId = all[2].ClaimId };
        var result = Map(all);
        Assert.Equal(SyntheticRevenueMapStatus.InvalidInput, result.State);
        Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData("other-tenant")]
    [InlineData("other-operator")]
    [InlineData("other-period")]
    [InlineData("real-income-event")]
    [InlineData("real-evidence")]
    [InlineData("invalid-revision")]
    [InlineData("missing-request-id")]
    [InlineData("unknown-stream")]
    [InlineData("unknown-component")]
    [InlineData("unknown-financial-check")]
    [InlineData("unsafe-period-ref")]
    public void MalformedOrCrossScopeClaimsFailWithoutProjection(string mode)
    {
        var all = Six();
        all[2] = mode switch
        {
            "other-tenant" => all[2] with { TenantRef = "test:other-tenant" },
            "other-operator" => all[2] with { OperatingScopeRef = "test:other-operator" },
            "other-period" => all[2] with {
                ClaimedFinancialPeriodRef = "test:other-period" },
            "real-income-event" => all[2] with {
                ClaimedUnderlyingRevenueEventRef = "bank:real-event" },
            "real-evidence" => all[2] with { UnverifiedEvidenceRef = "real:receipt" },
            "invalid-revision" => all[2] with { UnderlyingEventRevision = 0 },
            "missing-request-id" => all[2] with { ClaimId = Guid.Empty },
            "unknown-stream" => all[2] with { Stream = (SyntheticRevenueStream)99 },
            "unknown-component" => all[2] with { Component = (SyntheticRevenueComponent)22 },
            "unknown-financial-check" => all[2] with {
                ClaimedFinancialCustodianAuthority = (EvidenceCheck)88 },
            "unsafe-period-ref" => all[2] with { ClaimedFinancialPeriodRef = "test:a b" },
            _ => throw new ArgumentException()
        };
        var result = Map(all);
        Assert.Equal(SyntheticRevenueMapStatus.InvalidInput, result.State);
        Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("operator")]
    [InlineData("financial-period")]
    [InlineData("claims")]
    public void MissingPortfolioBoundaryNeverAllowsSourceReading(string input)
    {
        var all = Six();
        var read = input switch
        {
            "tenant" => SyntheticSixRevenueEvidenceMap.Inspect(
                null, Scope, Period, all),
            "operator" => SyntheticSixRevenueEvidenceMap.Inspect(
                Tenant, "real:operator", Period, all),
            "financial-period" => SyntheticSixRevenueEvidenceMap.Inspect(
                Tenant, Scope, "test:bad period", all),
            "claims" => Map(null),
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticRevenueMapStatus.InvalidInput, read.State);
        Assert.Empty(read.Sources);
    }

    [Fact]
    public void SourceClassificationOrderDoesNotInventTemporalRevenueOrAllocation()
    {
        var all = Six();
        var forward = Map(all);
        var reverse = Map(all.Reverse().ToArray());
        Assert.Equal(forward.State, reverse.State);
        Assert.Equal(forward.Sources.ToArray(), reverse.Sources.ToArray());
        Assert.False(reverse.EqualShareDistributionExecuted);
    }

    [Fact]
    public void MultipleDifferentDirectIncomeClaimsAreStillOnlyOneOfSixStreams()
    {
        var claims = Six().ToList();
        claims.Add(Claim(SyntheticRevenueStream.DirectTaranomIncome, 22,
            SyntheticRevenueComponent.MembershipSubscription));
        var result = Map(claims);
        Assert.Equal(SyntheticRevenueMapStatus.UnverifiedSixSourceClaimsOnly, result.State);
        Assert.Equal(6, result.Sources.Length);
        Assert.Equal(2, result.Sources[5].UnverifiedClaimCount);
        Assert.Equal(1, result.Sources[5].MembershipSubscriptionComponentClaimCount);
    }

    [Fact]
    public void NoAmountsRatesBenefitDatesOrActualMemberKeysOnRead()
    {
        string[] forbidden = [
            "Amount", "Balance", "RevenueTotal", "Cash", "Price", "ShareRate",
            "Percentage", "AllocationId", "OfficialMember", "Patient", "PaymentDate",
            "BookRevenue", "RealizedProfit", "Entitlement", "WalletPosting"
        ];
        foreach (var name in typeof(SyntheticRevenueEvidenceRead).GetProperties()
            .Concat(typeof(SyntheticRevenueLaneRead).GetProperties())
            .Select(x => x.Name))
            Assert.DoesNotContain(forbidden, term =>
                name.Contains(term, StringComparison.OrdinalIgnoreCase));
        var read = Map(Six());
        Assert.False(read.FinancialRevenueRecognized);
        Assert.False(read.TreatmentWalletCredited);
        Assert.False(read.AllocationAmountCalculated);
    }

    private static SyntheticRevenueOriginClaim Check(
        SyntheticRevenueOriginClaim claim, int which, EvidenceCheck status) =>
        which switch
        {
            0 => claim with { ClaimedLawfulSource = status },
            1 => claim with { ClaimedAccountingClassification = status },
            2 => claim with { ClaimedFinancialCustodianAuthority = status },
            3 => claim with { ClaimedIndependentAntiDoubleCountReview = status },
            _ => throw new ArgumentException()
        };
}
