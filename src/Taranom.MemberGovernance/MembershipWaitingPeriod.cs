namespace Taranom.MemberGovernance;

// Pure Business-rule observation. Only an authoritative .NET Membership owner may
// supply an official activation event and trusted observation clock in real runtime.
// "Elapsed" is NOT credit eligibility, spend authorization, activation or payment approval.
public enum WaitingPeriodState
{
    OfficialActivationMissing,
    ObservationBeforeActivation,
    StillWaiting,
    NinetyFullDaysElapsed
}

public readonly record struct WaitingPeriodObservation(
    WaitingPeriodState State,
    DateTimeOffset? ThresholdUtc);

public static class MembershipWaitingPeriod
{
    public static readonly TimeSpan RequiredElapsed = TimeSpan.FromDays(90);

    public static WaitingPeriodObservation Observe(
        DateTimeOffset? officialActivationAt,
        DateTimeOffset trustedObservedAt)
    {
        if (officialActivationAt is null)
            return new(WaitingPeriodState.OfficialActivationMissing, null);

        var activated = officialActivationAt.Value.ToUniversalTime();
        var observed = trustedObservedAt.ToUniversalTime();

        // Time conflict: never infer a valid completed period from future activation.
        if (observed < activated)
            return new(WaitingPeriodState.ObservationBeforeActivation, null);

        // Extremely distant dates whose threshold cannot be represented fail safely.
        if (activated.Ticks > DateTimeOffset.MaxValue.Ticks - RequiredElapsed.Ticks)
            return new(WaitingPeriodState.StillWaiting, null);

        var threshold = activated.Add(RequiredElapsed);
        return observed >= threshold
            ? new(WaitingPeriodState.NinetyFullDaysElapsed, threshold)
            : new(WaitingPeriodState.StillWaiting, threshold);
    }
}
