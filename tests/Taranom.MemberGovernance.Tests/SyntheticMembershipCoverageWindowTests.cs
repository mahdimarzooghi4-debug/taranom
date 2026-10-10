using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticMembershipCoverageWindowTests
{
    private const string Member = "test:member-one";
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Apr = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset May = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static SyntheticMembershipBoundary[] ClosedWindows() =>
    [
        new(1, Guid.NewGuid(), Member, SyntheticMembershipBoundaryKind.ActivationRecorded, Jan),
        new(2, Guid.NewGuid(), Member, SyntheticMembershipBoundaryKind.NonRenewalRecorded, Feb),
        new(3, Guid.NewGuid(), Member, SyntheticMembershipBoundaryKind.ReactivationRecorded, Mar),
        new(4, Guid.NewGuid(), Member, SyntheticMembershipBoundaryKind.NonRenewalRecorded, Apr)
    ];

    private static MembershipWindowObservation Observe(
        IReadOnlyList<SyntheticMembershipBoundary>? markers,
        DateTimeOffset from, DateTimeOffset to,
        string? subject = Member, DateTimeOffset? asOf = null) =>
        SyntheticMembershipCoverageWindow.Observe(subject, markers, from, to, asOf ?? May);

    [Fact]
    public void WholeHistoricalMembershipIntervalIsOnlyRecordedTemporalCoverage() =>
        Assert.Equal(MembershipWindowObservation.WithinClosedRecordedMembership,
            Observe(ClosedWindows(), Jan, Feb));

    [Fact]
    public void ClosedNonrenewalGapIsNeverReportedAsMembership() =>
        Assert.Equal(MembershipWindowObservation.WithinClosedRecordedGap,
            Observe(ClosedWindows(), Feb, Mar));

    [Fact]
    public void ReopenedMembershipStartsExactlyAtExplicitReactivation() =>
        Assert.Equal(MembershipWindowObservation.WithinClosedRecordedMembership,
            Observe(ClosedWindows(), Mar, Apr));

    [Fact]
    public void PriorMemberPeriodCanBeObservedAfterNonrenewalWithoutCreditingWallet() =>
        Assert.Equal(MembershipWindowObservation.WithinClosedRecordedMembership,
            Observe(ClosedWindows(), Jan.AddDays(3), Jan.AddDays(7), asOf: May));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CrossingRecordedBoundariesNeverGetsUnqualifiedMemberClassification(int range)
    {
        var segments = new[] { (Jan.AddDays(1), Feb.AddDays(1)),
            (Feb.AddDays(1), Mar.AddDays(1)),
            (Jan.AddDays(1), Apr) };
        var query = segments[range];
        Assert.Equal(MembershipWindowObservation.CrossesRecordedBoundary,
            Observe(ClosedWindows(), query.Item1, query.Item2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnknownBeforeOrAfterOrOpenWindowNeverBecomesPositive(int variant)
    {
        var history = ClosedWindows();
        var from = variant == 0 ? Jan.AddTicks(-1) : variant == 1 ? Apr : Jan;
        var to = variant == 0 ? Jan.AddDays(1) : variant == 1 ? Apr.AddDays(1) : Feb;
        var supplied = variant == 2 ? history[..1] : history;
        Assert.Equal(MembershipWindowObservation.EvidenceInsufficient,
            Observe(supplied, from, to));
    }

    [Fact]
    public void AnUnclosedNonrenewalCannotClaimPermanentGap()
    {
        var events = ClosedWindows()[..2];
        Assert.Equal(MembershipWindowObservation.EvidenceInsufficient,
            Observe(events, Feb, Feb.AddDays(10)));
    }

    [Fact]
    public void MissingTimelineDoesNotStartMembership()
    {
        Assert.Equal(MembershipWindowObservation.EvidenceInsufficient,
            Observe(null, Jan, Feb));
        Assert.Equal(MembershipWindowObservation.EvidenceInsufficient,
            Observe(Array.Empty<SyntheticMembershipBoundary>(), Jan, Feb));
    }

    [Theory]
    [InlineData("some-real-member")]
    [InlineData("test:")]
    [InlineData("test:bad space")]
    [InlineData("")]
    public void InvalidOrRealSubjectReferenceNeverPasses(string subject) =>
        Assert.Equal(MembershipWindowObservation.InvalidInput,
            Observe(ClosedWindows(), Jan, Feb, subject));

    [Fact]
    public void AnotherMemberCannotReuseOtherwiseWellFormedHistory() =>
        Assert.Equal(MembershipWindowObservation.InvalidHistory,
            Observe(ClosedWindows(), Jan, Feb, "test:other-member"));

    [Fact]
    public void InvalidFutureOrReverseQueryIsNotAValidHistoricalInterval()
    {
        var markers = ClosedWindows();
        Assert.Equal(MembershipWindowObservation.InvalidInput, Observe(markers, Feb, Feb));
        Assert.Equal(MembershipWindowObservation.InvalidInput, Observe(markers, Mar, Feb));
        Assert.Equal(MembershipWindowObservation.InvalidInput,
            Observe(markers, Jan, May, asOf: Apr));
    }

    [Theory]
    [InlineData("repeated-revision")]
    [InlineData("skipped-revision")]
    [InlineData("duplicate-id")]
    [InlineData("empty-id")]
    [InlineData("changed-subject")]
    [InlineData("reordered-instants")]
    [InlineData("same-instant")]
    [InlineData("event-after-observation")]
    [InlineData("invalid-event-kind")]
    [InlineData("double-nonrenewal")]
    public void CorruptOrContradictoryLineageFailsClosed(string mutation)
    {
        var history = ClosedWindows();
        history[1] = mutation switch
        {
            "repeated-revision" => history[1] with { Revision = 1 },
            "skipped-revision" => history[1] with { Revision = 3 },
            "duplicate-id" => history[1] with { EventId = history[0].EventId },
            "empty-id" => history[1] with { EventId = Guid.Empty },
            "changed-subject" => history[1] with { SubjectRef = "test:someone-else" },
            "reordered-instants" => history[1] with { EffectiveAt = Jan.AddTicks(-1) },
            "same-instant" => history[1] with { EffectiveAt = Jan },
            "event-after-observation" => history[1] with { EffectiveAt = May.AddDays(1) },
            "invalid-event-kind" => history[1] with { Kind = (SyntheticMembershipBoundaryKind)99 },
            "double-nonrenewal" => history[1] with { Kind = SyntheticMembershipBoundaryKind.ReactivationRecorded },
            _ => throw new ArgumentException("Unknown test mutation.")
        };
        Assert.Equal(MembershipWindowObservation.InvalidHistory,
            Observe(history, Jan, Feb));
    }

    [Fact]
    public void InitialActivationCannotBeInferredFromNonrenewalOrReactivation()
    {
        foreach (var invalid in new[] {
            SyntheticMembershipBoundaryKind.NonRenewalRecorded,
            SyntheticMembershipBoundaryKind.ReactivationRecorded })
        {
            var history = ClosedWindows();
            history[0] = history[0] with { Kind = invalid };
            Assert.Equal(MembershipWindowObservation.InvalidHistory,
                Observe(history, Jan, Feb));
        }
    }

    [Fact]
    public void SameInstantsInOtherUtcOffsetsHaveSameHistoricalMeaning()
    {
        var history = ClosedWindows();
        var from = Jan.AddHours(5).ToOffset(TimeSpan.FromHours(4));
        var to = Jan.AddDays(5).ToOffset(TimeSpan.FromHours(-7));
        Assert.Equal(MembershipWindowObservation.WithinClosedRecordedMembership,
            Observe(history, from, to, asOf: May.ToOffset(TimeSpan.FromHours(2))));
    }

    [Fact]
    public void SemanticContractHasNoFinancialCreditOrPositivePermissionStatus()
    {
        Assert.DoesNotContain(Enum.GetNames<MembershipWindowObservation>(),
            name => name.Contains("Approved") || name.Contains("Authorized") ||
                    name.Contains("Eligible") || name.Contains("Paid") ||
                    name.Contains("Credited"));
        Assert.DoesNotContain(typeof(SyntheticMembershipBoundary).GetProperties(),
            p => p.Name.Contains("Amount") || p.Name.Contains("Balance") ||
                p.Name.Contains("Payment") || p.Name.Contains("Bank"));
    }
}
