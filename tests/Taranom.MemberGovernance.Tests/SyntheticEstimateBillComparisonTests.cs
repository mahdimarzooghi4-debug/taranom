using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticEstimateBillComparisonTests
{
    private const string Member = "test:member-a";
    private const string Treatment = "test:treatment-a";
    private const string Link = "test:unverified-correspondence-a";
    private static readonly Guid PreviewId = Guid.Parse("00000000-0000-4000-8000-000000000050");
    private static readonly Guid Packet = Guid.Parse("00000000-0000-4000-8000-000000000051");

    private static SyntheticCostLine PreviewLine(SyntheticCostComponent c,
        SyntheticCostEvidenceState state = SyntheticCostEvidenceState.ProvisionalClaim,
        decimal amount = 10m, string currency = "IRR") =>
        state switch
        {
            SyntheticCostEvidenceState.Missing =>
                new(c, state, null, null, null, Guid.Empty, 0),
            SyntheticCostEvidenceState.ConflictingClaims =>
                new(c, state, null, null, "test:disputed-cost", Guid.NewGuid(), 1),
            _ => new(c, state, amount, currency, "test:claimed-cost-source", Guid.NewGuid(), 1)
        };

    private static SyntheticCostLine[] Lines() =>
        Enum.GetValues<SyntheticCostComponent>().Select(c => PreviewLine(c)).ToArray();

    private static SyntheticCostPreview Preview(SyntheticCostLine[]? lines = null,
        string member = Member, long revision = 1) =>
        SyntheticCostPreviewBuilder.Build(member, PreviewId, revision, lines ?? Lines());

    private static SyntheticBillClaimedIssuer Issuer(SyntheticBillLane lane) =>
        lane switch
        {
            SyntheticBillLane.ProviderService or SyntheticBillLane.MemberRemainder =>
                SyntheticBillClaimedIssuer.ProviderDocumentIssuer,
            SyntheticBillLane.PrimaryInsurance => SyntheticBillClaimedIssuer.PrimaryInsurer,
            SyntheticBillLane.SupplementalInsurance =>
                SyntheticBillClaimedIssuer.SupplementalInsurer,
            _ => SyntheticBillClaimedIssuer.LicensedCreditCustodian
        };

    private static SyntheticBillEvidence[] Bill(decimal amount = 10m, string currency = "IRR") =>
        Enum.GetValues<SyntheticBillLane>().Select(l => new SyntheticBillEvidence(
            Member, Treatment, Packet, l, Issuer(l),
            SyntheticBillEvidenceChange.UnverifiedClaim,
            Guid.NewGuid(), Guid.Empty, 1, "test:bill-issuer-claim", 1,
            amount, currency)).ToArray();

    private static CostAwarenessSnapshot History(SyntheticCostPreview preview,
        params CostAwarenessChoice[] choices)
    {
        var history = CostAwarenessSnapshot.FromPreview(preview)!;
        foreach (var choice in choices)
        {
            var result = CostAwarenessRecorder.Record(history,
                new CostAwarenessCommand(Guid.NewGuid(), history.Revision,
                    preview.MemberRef!, preview.PreviewId, preview.PreviewRevision,
                    preview.Fingerprint!, choice));
            Assert.Equal(CostAwarenessOutcome.RecordedUnverified, result.Outcome);
            history = result.Snapshot!;
        }
        return history;
    }

    private static SyntheticEstimateBillComparisonRead Compare(SyntheticCostPreview preview,
        SyntheticBillEvidence[]? bill = null, CostAwarenessSnapshot? history = null,
        string? treatment = Treatment, string? link = Link,
        long billRevision = 1, Guid? packet = null) =>
        SyntheticEstimateBillComparison.Inspect(preview, history ?? History(preview),
            treatment, link, packet ?? Packet, billRevision, bill ?? Bill());

    [Fact]
    public void MatchingFiveClaimedAmountsNeverBecomeOfficialBill()
    {
        var preview = Preview();
        var result = Compare(preview, history: History(preview, CostAwarenessChoice.AcknowledgedUncertainty));
        Assert.Equal(SyntheticEstimateBillComparisonState.UnverifiedClaimsOnly, result.State);
        Assert.Equal(5, result.Lines.Length);
        Assert.All(result.Lines, x => Assert.Equal(
            SyntheticEstimateBillLineState.UnverifiedSameAmount, x.State));
        Assert.Equal(CostAwarenessChoice.AcknowledgedUncertainty, result.PreviousUnverifiedChoice);
        Assert.False(result.OfficialBillConfirmed);
        Assert.False(result.PaymentOrRefundAuthorized);
        Assert.False(result.WalletMutated);
        Assert.False(result.ClinicalConsentGranted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EachClaimedBillLaneDifferenceIsReportedWithoutDebtOrRefund(int lane)
    {
        var p = Preview();
        var bill = Bill();
        bill[lane - 1] = bill[lane - 1] with { UnverifiedAmount = 17m };
        var result = Compare(p, bill);
        Assert.Equal(SyntheticEstimateBillComparisonState.UnverifiedClaimDifferences, result.State);
        Assert.Single(result.Lines.Where(x =>
            x.State == SyntheticEstimateBillLineState.UnverifiedDifferentAmount));
        Assert.Equal((SyntheticCostComponent)lane, result.Lines[lane - 1].Component);
        Assert.False(result.PaymentOrRefundAuthorized);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void MissingAnyPreviewComponentIsUnknownNotZero(int lane)
    {
        var lines = Lines();
        lines[lane - 1] = PreviewLine((SyntheticCostComponent)lane, SyntheticCostEvidenceState.Missing);
        var p = Preview(lines);
        var result = Compare(p);
        Assert.Equal(SyntheticEstimateBillComparisonState.IncompleteOrDisputedSources, result.State);
        Assert.Equal(SyntheticEstimateBillLineState.PreviewSourceMissing,
            result.Lines[lane - 1].State);
        Assert.False(result.WalletMutated);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ConflictedPreviewCannotBeDescribedAsDefiniteNumericDifference(int lane)
    {
        var lines = Lines();
        lines[lane - 1] = PreviewLine((SyntheticCostComponent)lane,
            SyntheticCostEvidenceState.ConflictingClaims);
        var result = Compare(Preview(lines));
        Assert.Equal(SyntheticEstimateBillComparisonState.IncompleteOrDisputedSources, result.State);
        Assert.Equal(SyntheticEstimateBillLineState.PreviewSourceConflicted,
            result.Lines[lane - 1].State);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DisputedBillLaneMasksPreviousNumericClaim(int lane)
    {
        var p = Preview();
        var bill = Bill();
        var old = bill[lane - 1];
        bill = [.. bill, old with {
            EvidenceId = Guid.NewGuid(), SupersedesEvidenceId = old.EvidenceId,
            LaneRevision = 2, SourceRevision = 2,
            Change = SyntheticBillEvidenceChange.Disputed,
            UnverifiedAmount = null, CurrencyCode = null
        }];
        var result = Compare(p, bill);
        Assert.Equal(SyntheticEstimateBillComparisonState.IncompleteOrDisputedSources, result.State);
        Assert.Equal(SyntheticEstimateBillLineState.BillSourceDisputed,
            result.Lines[lane - 1].State);
        Assert.False(result.PaymentOrRefundAuthorized);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void WithdrawnBillClaimDoesNotCarryOriginalAmount(int lane)
    {
        var p = Preview();
        var bill = Bill();
        var old = bill[lane - 1];
        bill = [.. bill, old with {
            EvidenceId = Guid.NewGuid(), SupersedesEvidenceId = old.EvidenceId,
            LaneRevision = 2, SourceRevision = 2,
            Change = SyntheticBillEvidenceChange.Withdrawn,
            UnverifiedAmount = null, CurrencyCode = null
        }];
        var result = Compare(p, bill);
        Assert.Equal(SyntheticEstimateBillLineState.BillSourceMissingOrWithdrawn,
            result.Lines[lane - 1].State);
        Assert.Equal(SyntheticEstimateBillComparisonState.IncompleteOrDisputedSources, result.State);
    }

    [Fact]
    public void ActualClaimedZeroIsDistinctFromUnknown()
    {
        var lines = Lines();
        lines[0] = lines[0] with { ClaimedAmount = 0m };
        var same = Bill(); same[0] = same[0] with { UnverifiedAmount = 0m };
        Assert.Equal(SyntheticEstimateBillLineState.UnverifiedSameAmount,
            Compare(Preview(lines), same).Lines[0].State);
        lines[0] = PreviewLine(SyntheticCostComponent.ProviderService,
            SyntheticCostEvidenceState.Missing);
        Assert.Equal(SyntheticEstimateBillLineState.PreviewSourceMissing,
            Compare(Preview(lines), same).Lines[0].State);
    }

    [Fact]
    public void CurrencyDifferenceCannotBeNetSettledOrExchangedAutomatically()
    {
        var p = Preview();
        var b = Bill(currency: "USD");
        var result = Compare(p, b);
        Assert.Equal(SyntheticEstimateBillComparisonState.IncompleteOrDisputedSources, result.State);
        Assert.All(result.Lines, x =>
            Assert.Equal(SyntheticEstimateBillLineState.UnverifiedCurrencyDifference, x.State));
    }

    [Theory]
    [InlineData("no-bill")]
    [InlineData("invalid-bill-version")]
    [InlineData("no-treatment")]
    [InlineData("no-packet")]
    [InlineData("foreign-treatment")]
    [InlineData("bad-issuer")]
    [InlineData("bad-event-id")]
    [InlineData("bad-source")]
    [InlineData("negative-bill")]
    [InlineData("invalid-preview")]
    [InlineData("forged-preview-fingerprint")]
    public void InvalidSourceDoesNotYieldMemberBillOrProjection(string mode)
    {
        var p = Preview();
        var b = Bill();
        string? treatment = Treatment;
        var packet = Packet;
        var revision = 1L;
        if (mode == "no-bill") b = null!;
        if (mode == "invalid-bill-version") revision = 0;
        if (mode == "no-treatment") treatment = null;
        if (mode == "no-packet") packet = Guid.Empty;
        if (mode == "foreign-treatment") treatment = "test:unrelated-treatment";
        if (mode == "bad-issuer") b[1] = b[1] with {
            ClaimedIssuer = SyntheticBillClaimedIssuer.ProviderDocumentIssuer };
        if (mode == "bad-event-id") b[2] = b[2] with { EvidenceId = Guid.Empty };
        if (mode == "bad-source") b[2] = b[2] with { UnverifiedSourceRef = "real:source" };
        if (mode == "negative-bill") b[2] = b[2] with { UnverifiedAmount = -1m };
        if (mode == "invalid-preview") p = default;
        if (mode == "forged-preview-fingerprint") p = p with { Fingerprint = new string('0', 64) };
        var result = Compare(p, b, treatment:treatment, packet:packet,billRevision:revision);
        Assert.Equal(SyntheticEstimateBillComparisonState.InvalidSource, result.State);
        Assert.Null(result.MemberRef);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void UnverifiedCorrespondenceReferenceIsMandatoryAndNotVerifiedLink()
    {
        var p= Preview();
        foreach (var link in new string?[] { null, "real:claim", "test:space here" })
        {
            var result = Compare(p, link:link);
            Assert.Equal(SyntheticEstimateBillComparisonState.ClaimLinkMissing, result.State);
            Assert.Null(result.MemberRef);
        }
    }

    [Theory]
    [InlineData("other-member")]
    [InlineData("different-treatment-event")]
    [InlineData("cross-packet")]
    public void CrossScopeBillEvidenceNeverMixesIntoMemberComparison(string mode)
    {
        var p = Preview();
        var b = Bill();
        b[0] = mode switch
        {
            "other-member" => b[0] with { MemberRef = "test:other-member" },
            "different-treatment-event" => b[0] with { TreatmentRef = "test:other-treatment" },
            "cross-packet" => b[0] with { PacketId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        var result = Compare(p, b);
        Assert.NotEqual(SyntheticEstimateBillComparisonState.UnverifiedClaimsOnly, result.State);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void ForgedChoiceHistoryCannotCrossHistoricalPreview()
    {
        var p = Preview();
        var other = Preview(revision:2);
        var result = Compare(p, history:History(other,CostAwarenessChoice.AcknowledgedUncertainty));
        Assert.Equal(SyntheticEstimateBillComparisonState.PriorChoiceHistoryInvalid, result.State);
    }

    [Fact]
    public void NoChoiceMeansNoImpliedAcknowledgmentOrLegalConsent()
    {
        var p=Preview();
        var result=Compare(p);
        Assert.Null(result.PreviousUnverifiedChoice);
        Assert.False(result.ClinicalConsentGranted);
        Assert.False(result.PaymentOrRefundAuthorized);
    }

    [Fact]
    public void RecordedChoiceDoesNotBindDifferentPostServiceDocument()
    {
        var p=Preview();
        var history=History(p,CostAwarenessChoice.AcknowledgedUncertainty);
        var result=Compare(p, Bill(amount:31m), history);
        Assert.Equal(CostAwarenessChoice.AcknowledgedUncertainty,
            result.PreviousUnverifiedChoice);
        Assert.Equal(SyntheticEstimateBillComparisonState.UnverifiedClaimDifferences, result.State);
        Assert.False(result.OfficialBillConfirmed);
        Assert.False(result.PaymentOrRefundAuthorized);
        Assert.False(result.WalletMutated);
    }

    [Fact]
    public void ComparisonCannotExpressRealOverchargeOrApprovedSettlement()
    {
        var types = typeof(SyntheticEstimateBillComparisonRead).GetProperties()
            .Select(x=>x.Name).ToArray();
        foreach(var prohibited in new []{"Overcharge","Liability","RefundAmount",
            "ApprovedInsurer","TreatmentAuthorized","PaidAt","ConfirmedDebt"})
            Assert.DoesNotContain(types, x=>x.Contains(prohibited,StringComparison.OrdinalIgnoreCase));
    }
}
