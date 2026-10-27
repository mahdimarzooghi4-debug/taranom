namespace Taranom.MemberGovernance;

// Provider-neutral read contract. These values are source assertions; this
// contract does not authenticate a source or make a slot bookable.
internal sealed record AppointmentScheduleQuery(
    string CenterRef,
    string? DepartmentRef,
    string? RequestedDoctorRef,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

internal sealed record AppointmentScheduleSlotClaim(
    string SlotRef,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

internal sealed record AppointmentScheduleObservation(
    string SourceRef,
    string SourceRevision,
    string CenterRef,
    string? DepartmentRef,
    string? RequestedDoctorRef,
    DateTimeOffset ObservedAt,
    IReadOnlyList<AppointmentScheduleSlotClaim> Slots);

internal interface IAppointmentScheduleSource
{
    ValueTask<AppointmentScheduleObservation?> ReadAsync(
        AppointmentScheduleQuery query,
        CancellationToken cancellationToken);
}

internal enum AppointmentScheduleReadOutcome
{
    SourceNotConfigured = 1,
    SourceClaimReceived = 2,
    SourceUnavailable = 3,
    InvalidQuery = 4,
    InvalidSourceResponse = 5
}

internal sealed record AppointmentScheduleReadResult(
    AppointmentScheduleReadOutcome Outcome,
    IReadOnlyList<AppointmentScheduleSlotClaim> SourceClaimedSlots,
    bool SourceAuthenticated,
    bool SlotAvailabilityVerified,
    bool BookingConfirmed,
    bool ClinicalActionAllowed)
{
    internal static AppointmentScheduleReadResult Denied(
        AppointmentScheduleReadOutcome outcome) =>
        new(outcome, Array.Empty<AppointmentScheduleSlotClaim>(),
            SourceAuthenticated: false,
            SlotAvailabilityVerified: false,
            BookingConfirmed: false,
            ClinicalActionAllowed: false);
}

// This seam is intentionally inert when no authorized provider adapter has
// been supplied. A returned observation is retained as an unverified claim;
// callers must not render it as verified availability or use it to book.
internal sealed class AppointmentScheduleReadService(
    IAppointmentScheduleSource? source)
{
    private const int MaximumSourceClaims = 50;

    internal async ValueTask<AppointmentScheduleReadResult> ReadAsync(
        AppointmentScheduleQuery? query,
        CancellationToken cancellationToken = default)
    {
        if (query is null || !ValidRef(query.CenterRef) ||
            query.DepartmentRef is not null && !ValidRef(query.DepartmentRef) ||
            query.RequestedDoctorRef is not null && !ValidRef(query.RequestedDoctorRef) ||
            query.StartsAt == default || query.EndsAt <= query.StartsAt)
            return AppointmentScheduleReadResult.Denied(
                AppointmentScheduleReadOutcome.InvalidQuery);

        if (source is null)
            return AppointmentScheduleReadResult.Denied(
                AppointmentScheduleReadOutcome.SourceNotConfigured);

        try
        {
            var observation = await source.ReadAsync(query, cancellationToken);
            if (observation is null)
                return AppointmentScheduleReadResult.Denied(
                    AppointmentScheduleReadOutcome.SourceUnavailable);

            if (!ValidObservation(query, observation))
                return AppointmentScheduleReadResult.Denied(
                    AppointmentScheduleReadOutcome.InvalidSourceResponse);

            return new(AppointmentScheduleReadOutcome.SourceClaimReceived,
                observation.Slots.ToArray(),
                SourceAuthenticated: false,
                SlotAvailabilityVerified: false,
                BookingConfirmed: false,
                ClinicalActionAllowed: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AppointmentScheduleReadResult.Denied(
                AppointmentScheduleReadOutcome.SourceUnavailable);
        }
    }

    private static bool ValidObservation(
        AppointmentScheduleQuery query,
        AppointmentScheduleObservation value)
    {
        if (!ValidRef(value.SourceRef) || !ValidRef(value.SourceRevision) ||
            value.CenterRef != query.CenterRef ||
            value.DepartmentRef != query.DepartmentRef ||
            value.RequestedDoctorRef != query.RequestedDoctorRef ||
            value.ObservedAt == default ||
            value.Slots is null || value.Slots.Count > MaximumSourceClaims)
            return false;

        var refs = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? previousStart = null;
        foreach (var slot in value.Slots)
        {
            if (slot is null || !ValidRef(slot.SlotRef) ||
                !refs.Add(slot.SlotRef) || slot.StartsAt == default ||
                slot.EndsAt <= slot.StartsAt ||
                slot.StartsAt < query.StartsAt || slot.EndsAt > query.EndsAt ||
                previousStart is not null && slot.StartsAt < previousStart)
                return false;
            previousStart = slot.StartsAt;
        }

        return true;
    }

    private static bool ValidRef(string? value) =>
        value is { Length: > 0 and <= 128 } &&
        value.All(ch => !char.IsControl(ch));
}
