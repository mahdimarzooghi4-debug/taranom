using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticCreditPeriodReviewProjectorTests
{
    private const string Member = "test:member-one";
    private static readonly DateTimeOffset January =
        new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset February = January.AddDays(31);
    private static readonly DateTimeOffset March = February.AddDays(28);
    private static readonly DateTimeOffset April = March.AddDays(31);
    private static readonly DateTimeOffset May = April.AddDays(30);
    private static readonly Guid Packet = Guid.Parse("00000000-0000-4000-8000-000000000043");

    private static SyntheticMembershipBoundary[] Timeline() =>
    [
        new(1, Guid.Parse("00000000-0000-4000-8000-000000000001"),
            Member, SyntheticMembershipBoundaryKind.ActivationRecorded, January),
        new(2, Guid.Parse("00000000-0000-4000-8000-000000000002"),
            Member, SyntheticMembershipBoundaryKind.NonRenewalRecorded, February),
        new(3, Guid.Parse("00000000-0000-4000-8000-000000000003"),
            Member, SyntheticMembershipBoundaryKind.ReactivationRecorded, March),
        new(4, Guid.Parse("00000000-0000-4000-8000-000000000004"),
            Member, SyntheticMembershipBoundaryKind.NonRenewalRecorded, April)
    ];

    private static SyntheticCreditPeriodClaim Claim(
        DateTimeOffset? start = null, DateTimeOffset? end = null,
        DateTimeOffset? recorded = null,
        EvidenceCheck review = EvidenceCheck.Missing) =>
        new(Packet, Member, "test:unverified-income-source", 1,
            start ?? January.AddDays(1), end ?? January.AddDays(9),
            recorded ?? May, review);

    private static SyntheticCreditPortfolioRead Build(
        IReadOnlyList<SyntheticCreditPeriodClaim>? claims,
        IReadOnlyList<SyntheticMembershipBoundary>? history = null,
        string? member = Member, DateTimeOffset? observed = null) =>
        SyntheticCreditPeriodReviewProjector.Build(member,
            history ?? Timeline(), claims, observed ?? May);

    [Fact]
    public void LateBenefitDuringClosedMembershipRemainsUnverifiedHumanWork()
    {
        var view = Build([Claim(review: EvidenceCheck.Confirmed)]);
        Assert.Equal(SyntheticCreditPortfolioState.UnverifiedClaimsOnly, view.State);
        Assert.Equal(SyntheticCreditClaimObservation.HistoricalMembershipEvidencePresentOnly,
            Assert.Single(view.Claims).Observation);
        Assert.True(Assert.Single(view.Claims).RecordedAfterAttributionPeriod);
        Assert.Equal(WaitingPeriodState.NinetyFullDaysElapsed,
            view.HypotheticalInitialWait!.Value.State);
        Assert.Equal(64, view.Fingerprint!.Length);
    }

    [Fact]
    public void MissingOrRejectedReviewNeverConfersFinancialEntitlement()
    {
        Assert.Equal(SyntheticCreditClaimObservation.HistoricalMembershipReviewMissing,
            Assert.Single(Build([Claim()]).Claims).Observation);
        Assert.Equal(SyntheticCreditClaimObservation.HistoricalMembershipReviewRejected,
            Assert.Single(Build([Claim(review: EvidenceCheck.Rejected)]).Claims).Observation);
    }

    [Fact]
    public void ClosedNonrenewalGapDoesNotCreateNewCreditEvenWithClaimedReview()
    {
        var input = Claim(February.AddDays(1), February.AddDays(7),
            May, EvidenceCheck.Confirmed);
        var result = Assert.Single(Build([input]).Claims);
        Assert.Equal(MembershipWindowObservation.WithinClosedRecordedGap,
            result.WindowObservation);
        Assert.Equal(SyntheticCreditClaimObservation.ClaimedNonMembershipPeriod,
            result.Observation);
    }

    [Fact]
    public void ExplicitReactivationHasDistinctUnverifiedMembershipInterval()
    {
        var claim = Claim(March.AddDays(2), March.AddDays(12),
            May, EvidenceCheck.Confirmed);
        Assert.Equal(SyntheticCreditClaimObservation.HistoricalMembershipEvidencePresentOnly,
            Assert.Single(Build([claim]).Claims).Observation);
    }

    [Fact]
    public void MixedPeriodsCannotBeSilentlySplitOrCredited()
    {
        var claim = Claim(January.AddDays(26), February.AddDays(2),
            May, EvidenceCheck.Confirmed);
        Assert.Equal(SyntheticCreditClaimObservation.CrossesMembershipBoundary,
            Assert.Single(Build([claim]).Claims).Observation);
    }

    [Fact]
    public void OpenPresentMembershipDoesNotGetAssumedFromLastRecordedMarker()
    {
        var claim = Claim(April, April.AddDays(4), May, EvidenceCheck.Confirmed);
        Assert.Equal(SyntheticCreditClaimObservation.TimelineEvidenceInsufficient,
            Assert.Single(Build([claim]).Claims).Observation);
    }

    [Fact]
    public void PrematureBenefitClaimNeverEstablishesFinishedPeriod()
    {
        var claim = Claim(recorded: January.AddDays(5), review: EvidenceCheck.Confirmed);
        Assert.Equal(SyntheticCreditClaimObservation.PrematurePeriodClaim,
            Assert.Single(Build([claim]).Claims).Observation);
    }

    [Fact]
    public void InitialWaitIsNotRestartedByReactivation()
    {
        var view = Build([Claim(March.AddDays(1), March.AddDays(4),
            recorded: April.AddDays(1))], observed: April.AddDays(2));
        Assert.Equal(January.AddDays(90), view.HypotheticalInitialWait!.Value.ThresholdUtc);
    }

    [Fact]
    public void InitialWaitIsOnlyATemporalReadNeverWalletSpendingPermission()
    {
        var view = Build(Array.Empty<SyntheticCreditPeriodClaim>(),
            history: Timeline()[..2], observed: January.AddDays(40));
        Assert.Equal(WaitingPeriodState.StillWaiting, view.HypotheticalInitialWait!.Value.State);
        Assert.Empty(view.Claims);
        Assert.DoesNotContain(Enum.GetNames<SyntheticCreditPortfolioState>(),
            s => s.Contains("Approved") || s.Contains("Paid") || s.Contains("Eligible"));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("same-source-period")]
    public void DuplicateClaimOrDuplicateAttributionSourceFailsClosed(string mutation)
    {
        var x = Claim();
        var y = mutation == "duplicate-id" ? x
            : x with { ClaimId = Guid.NewGuid() };
        var result = Build([x, y]);
        Assert.Equal(SyntheticCreditPortfolioState.Invalid, result.State);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public void SeparateSourceVersionsAreNeverAutomaticallyCombinedIntoAnAmount()
    {
        var a = Claim();
        var b = a with { ClaimId = Guid.NewGuid(), SourceRevision = 2 };
        Assert.Equal(2, Build([a,b]).Claims.Length);
        Assert.DoesNotContain(typeof(SyntheticCreditPortfolioRead).GetProperties(),
            p => p.Name.Contains("Amount") || p.Name.Contains("Balance") ||
                 p.Name.Contains("Wallet") || p.Name.Contains("Posting"));
    }

    [Fact]
    public void PermutationDoesNotChangeFingerprintButSourcesAndReviewChangesDo()
    {
        var a = Claim();
        var b = a with { ClaimId = Guid.NewGuid(), SourceRevision = 2 };
        var first = Build([a,b]);
        Assert.Equal(first.Fingerprint, Build([b,a]).Fingerprint);
        Assert.NotEqual(first.Fingerprint,
            Build([a with { ClaimedIndependentEvidenceReview = EvidenceCheck.Rejected },b]).Fingerprint);
        Assert.NotEqual(first.Fingerprint,
            Build([a with { UnverifiedIncomeSourceRef = "test:other-source" },b]).Fingerprint);
    }

    [Theory]
    [InlineData("bad-member")]
    [InlineData("wrong-member")]
    [InlineData("real-source")]
    [InlineData("missing-source")]
    [InlineData("missing-id")]
    [InlineData("invalid-source-revision")]
    [InlineData("reverse-period")]
    [InlineData("future-period")]
    [InlineData("future-recording")]
    [InlineData("unknown-review")]
    public void InvalidClaimInputReturnsNoReadOrFingerprint(string mutation)
    {
        var c = Claim();
        var result = mutation switch
        {
            "bad-member" => Build([c], member:"real:patient"),
            "wrong-member" => Build([c with {MemberRef="test:someone-else"}]),
            "real-source" => Build([c with {UnverifiedIncomeSourceRef="bank:real"}]),
            "missing-source" => Build([c with {UnverifiedIncomeSourceRef=null}]),
            "missing-id" => Build([c with {ClaimId=Guid.Empty}]),
            "invalid-source-revision" => Build([c with {SourceRevision=0}]),
            "reverse-period" => Build([c with {PeriodEndExclusive=c.PeriodStartInclusive}]),
            "future-period" => Build([c with {PeriodEndExclusive=May.AddDays(1)}]),
            "future-recording" => Build([c with {ClaimRecordedAt=May.AddTicks(1)}]),
            "unknown-review" => Build([c with {ClaimedIndependentEvidenceReview=(EvidenceCheck)99}]),
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticCreditPortfolioState.Invalid, result.State);
        Assert.Empty(result.Claims);
        Assert.Null(result.Fingerprint);
    }

    [Theory]
    [InlineData("corrupt-history")]
    [InlineData("out-of-order-history")]
    [InlineData("wrong-member-history")]
    [InlineData("duplicate-marker")]
    public void CorruptedTimelineCannotClaimWaitingOrAttribution(string mutation)
    {
        var h = Timeline();
        h[1] = mutation switch
        {
            "corrupt-history" => h[1] with { Revision=8 },
            "out-of-order-history" => h[1] with { EffectiveAt=January },
            "wrong-member-history" => h[1] with { SubjectRef="test:another" },
            "duplicate-marker" => h[1] with { EventId=h[0].EventId },
            _ => throw new ArgumentException()
        };
        var result = Build([Claim()], history:h);
        Assert.Equal(SyntheticCreditPortfolioState.HistoryInvalid, result.State);
        Assert.Null(result.HypotheticalInitialWait);
    }

    [Fact]
    public void AbsentOrOpenTimelineCannotPretendOfficialMembership()
    {
        Assert.Equal(SyntheticCreditPortfolioState.EvidenceInsufficient,
            Build([Claim()], history:Array.Empty<SyntheticMembershipBoundary>()).State);
        Assert.Equal(SyntheticCreditPortfolioState.EvidenceInsufficient,
            Build([Claim()], history:Timeline()[..1]).State);
        Assert.Equal(SyntheticCreditPortfolioState.Invalid, Build(null).State);
    }

    [Fact]
    public void NoFinancialOrMedicalSideEffectsAreExposed()
    {
        Assert.DoesNotContain(typeof(SyntheticCreditClaimRead).GetProperties(), p =>
            p.Name.Contains("Amount") || p.Name.Contains("Approved") ||
            p.Name.Contains("Paid") || p.Name.Contains("Wallet") ||
            p.Name.Contains("Posted") || p.Name.Contains("Debt"));
    }
}
