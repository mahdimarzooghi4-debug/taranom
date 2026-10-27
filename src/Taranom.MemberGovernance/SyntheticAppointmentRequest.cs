using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// Synthetic-only intent to request a visit with a member-specified provider.
// This contract has no confirmed/placed appointment state.
internal enum SyntheticAppointmentRequestOutcome
{
    RecordedRequest = 1,
    Replayed = 2,
    Invalid = 3,
    ConflictingReplay = 4,
    StaleRevision = 5
}

internal readonly record struct SyntheticAppointmentRequestCommand(
    Guid RequestId,
    long ExpectedRevision,
    string MemberRef,
    string CenterRef,
    string DepartmentRef,
    string RequestedDoctorRef,
    string SlotRef);

internal readonly record struct SyntheticAppointmentRequestEntry(
    long Revision,
    SyntheticAppointmentRequestCommand Command);

internal sealed class SyntheticAppointmentRequestSnapshot
{
    internal static SyntheticAppointmentRequestSnapshot Empty { get; } =
        new(0, ImmutableArray<SyntheticAppointmentRequestEntry>.Empty);

    private SyntheticAppointmentRequestSnapshot(
        long revision, ImmutableArray<SyntheticAppointmentRequestEntry> history)
    {
        Revision = revision;
        History = history;
    }

    internal long Revision { get; }
    internal ImmutableArray<SyntheticAppointmentRequestEntry> History { get; }

    internal SyntheticAppointmentRequestSnapshot Append(
        SyntheticAppointmentRequestCommand command) =>
        new(checked(Revision + 1),
            History.Add(new SyntheticAppointmentRequestEntry(Revision + 1, command)));
}

// Accepts only bounded test references. A recorded request is not a real member
// action, provider credential, available slot, accepted booking, or clinical visit.
internal static class SyntheticAppointmentRequestDomain
{
    internal static SyntheticAppointmentRequestChange Apply(
        SyntheticAppointmentRequestSnapshot? current,
        SyntheticAppointmentRequestCommand input)
    {
        var snapshot = current ?? SyntheticAppointmentRequestSnapshot.Empty;
        if (current is null || input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 || input.ExpectedRevision == long.MaxValue ||
            !TestRef(input.MemberRef) || !TestRef(input.CenterRef) ||
            !TestRef(input.DepartmentRef) || !TestRef(input.RequestedDoctorRef) ||
            !TestRef(input.SlotRef))
            return new(SyntheticAppointmentRequestOutcome.Invalid, snapshot);

        // An exact retry is idempotent. Reusing a request ID with altered
        // center, department, doctor, slot, member, or revision is a conflict.
        foreach (var prior in snapshot.History)
        {
            if (prior.Command.RequestId != input.RequestId) continue;
            return new(prior.Command == input
                ? SyntheticAppointmentRequestOutcome.Replayed
                : SyntheticAppointmentRequestOutcome.ConflictingReplay, snapshot);
        }

        if (input.ExpectedRevision != snapshot.Revision)
            return new(SyntheticAppointmentRequestOutcome.StaleRevision, snapshot);

        return new(SyntheticAppointmentRequestOutcome.RecordedRequest,
            snapshot.Append(input));
    }

    private static bool TestRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}

internal readonly record struct SyntheticAppointmentRequestChange(
    SyntheticAppointmentRequestOutcome Outcome,
    SyntheticAppointmentRequestSnapshot Snapshot);

internal readonly record struct SyntheticAppointmentRequestRead(
    long Revision,
    int RequestCount,
    IReadOnlyList<SyntheticAppointmentRequestEntry> Requests,
    bool MemberAuthenticated,
    bool CenterAuthenticated,
    bool RequestedDoctorLicensed,
    bool SlotAvailabilityVerified,
    bool BookingConfirmed,
    bool ClinicalActionAllowed);

// Projection preserves the member-specified doctor reference and never
// infers that a provider accepted the request or that a slot was available.
internal static class SyntheticAppointmentRequestProjection
{
    internal static SyntheticAppointmentRequestRead Project(
        SyntheticAppointmentRequestSnapshot? snapshot)
    {
        var state = snapshot ?? SyntheticAppointmentRequestSnapshot.Empty;
        return new(state.Revision, state.History.Length, state.History,
            MemberAuthenticated: false,
            CenterAuthenticated: false,
            RequestedDoctorLicensed: false,
            SlotAvailabilityVerified: false,
            BookingConfirmed: false,
            ClinicalActionAllowed: false);
    }
}
