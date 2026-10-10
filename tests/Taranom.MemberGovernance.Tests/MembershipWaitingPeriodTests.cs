using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class MembershipWaitingPeriodTests
{
    private static readonly DateTimeOffset Activation =
        new(2026, 1, 1, 13, 15, 0, TimeSpan.FromHours(3));

    [Fact]
    public void MissingActivationNeverStartsWaitingPeriod()
    {
        var actual = MembershipWaitingPeriod.Observe(null, Activation.AddDays(1000));
        Assert.Equal(WaitingPeriodState.OfficialActivationMissing, actual.State);
        Assert.Null(actual.ThresholdUtc);
    }

    [Fact]
    public void AnObservationBeforeOfficialActivationIsNotValidWaitingEvidence()
    {
        var actual = MembershipWaitingPeriod.Observe(Activation, Activation.AddTicks(-1));
        Assert.Equal(WaitingPeriodState.ObservationBeforeActivation, actual.State);
        Assert.Null(actual.ThresholdUtc);
    }

    [Fact]
    public void StartingMomentIsNotEndOfWaitingPeriod()
    {
        var actual = MembershipWaitingPeriod.Observe(Activation, Activation);
        Assert.Equal(WaitingPeriodState.StillWaiting, actual.State);
    }

    [Fact]
    public void OneTickBeforeNinetyCompleteDaysIsStillWaiting()
    {
        var actual = MembershipWaitingPeriod.Observe(
            Activation, Activation.AddDays(90).AddTicks(-1));
        Assert.Equal(WaitingPeriodState.StillWaiting, actual.State);
    }

    [Fact]
    public void ExactlyNinetyCompleteDaysSatisfiesOnlyTheTemporalPrerequisite()
    {
        var actual = MembershipWaitingPeriod.Observe(Activation, Activation.AddDays(90));
        Assert.Equal(WaitingPeriodState.NinetyFullDaysElapsed, actual.State);
        Assert.Equal(Activation.ToUniversalTime().AddDays(90), actual.ThresholdUtc);
    }

    [Fact]
    public void AfterThresholdRemainsTemporallyElapsed()
    {
        var actual = MembershipWaitingPeriod.Observe(Activation, Activation.AddDays(200));
        Assert.Equal(WaitingPeriodState.NinetyFullDaysElapsed, actual.State);
    }

    [Fact]
    public void LocalCalendarMonthCountCannotReplaceNinetyFullDays()
    {
        // February 1 to May 1 in 2026 is only 89 complete days.
        var shortQuarter = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(WaitingPeriodState.StillWaiting,
            MembershipWaitingPeriod.Observe(shortQuarter, shortQuarter.AddMonths(3)).State);
        // January 1 to April 1 in 2026 happens to be exactly 90 days.
        Assert.Equal(WaitingPeriodState.NinetyFullDaysElapsed,
            MembershipWaitingPeriod.Observe(Activation, Activation.AddMonths(3)).State);
    }

    [Fact]
    public void OffsetChangeDoesNotModifyElapsedInstant()
    {
        var observedInDifferentOffset = Activation.AddDays(90).ToOffset(TimeSpan.FromHours(-4));
        var actual = MembershipWaitingPeriod.Observe(Activation, observedInDifferentOffset);
        Assert.Equal(WaitingPeriodState.NinetyFullDaysElapsed, actual.State);
    }

    [Fact]
    public void TwentyFourHourPeriodsIgnoreDaylightSavingChanges()
    {
        var beforeChange = new DateTimeOffset(2026, 3, 8, 1, 30, 0, TimeSpan.FromHours(-5));
        var observation = beforeChange.ToUniversalTime().AddDays(90)
            .ToOffset(TimeSpan.FromHours(-4));
        Assert.Equal(WaitingPeriodState.NinetyFullDaysElapsed,
            MembershipWaitingPeriod.Observe(beforeChange, observation).State);
    }

    [Fact]
    public void LeapDayIsNotAThreeMonthCalendarApproximation()
    {
        var leapActivation = new DateTimeOffset(2024, 2, 29, 16, 0, 0, TimeSpan.Zero);
        Assert.Equal(WaitingPeriodState.NinetyFullDaysElapsed,
            MembershipWaitingPeriod.Observe(leapActivation, leapActivation.AddDays(90)).State);
    }

    [Fact]
    public void ThresholdOverflowCannotClaimElapsed()
    {
        var actual = MembershipWaitingPeriod.Observe(
            DateTimeOffset.MaxValue.AddDays(-1), DateTimeOffset.MaxValue);
        Assert.Equal(WaitingPeriodState.StillWaiting, actual.State);
        Assert.Null(actual.ThresholdUtc);
    }

    [Fact]
    public void ContractRequiresPreciselyNinetyTwentyFourHourPeriods()
    {
        Assert.Equal(TimeSpan.FromHours(90 * 24), MembershipWaitingPeriod.RequiredElapsed);
    }
}
