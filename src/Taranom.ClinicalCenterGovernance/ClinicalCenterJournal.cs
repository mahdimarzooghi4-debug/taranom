using System.Collections.Immutable;

namespace Taranom.ClinicalCenterGovernance;

// Non-deployable, TEST-ONLY center/department/doctor affiliation vocabulary.
// This assembly contains no real clinician, professional licence, clinical
// access, center credential, contracted-provider approval, or medical data.
internal enum CenterCommandKind
{
    DefineDepartment = 1,
    RecordDoctorAffiliation = 2,
    AssignDoctorToDepartment = 3,
    EndDoctorAffiliation = 4,
    UnassignDoctorFromDepartment = 5
}

internal enum CenterEvidence
{
    Missing = 0,
    Confirmed = 1,
    Rejected = 2
}

internal enum CenterOutcome
{
    Applied = 1,
    Replayed = 2,
    Invalid = 3,
    ConflictingReplay = 4,
    ScopeDenied = 5,
    EvidenceDenied = 6,
    EvidenceMissing = 7,
    StaleRevision = 8,
    NotApplicable = 9
}

// The only affiliation that can be recorded is UNVERIFIED. A center manager
// cannot grant licence, membership activation, clinical or financial access.
internal enum DoctorAffiliation
{
    PendingIndependentVerification = 1,
    Ended = 2
}

internal readonly record struct CenterCommand(
    Guid RequestId,
    long ExpectedVersion,
    CenterCommandKind Kind,
    string? TargetCenterRef,
    string? ActingCenterRef,
    string? DepartmentRef,
    string? DoctorRef,
    CenterEvidence CurrentCenterManagerAuthority,
    CenterEvidence AuditPathAvailable);

internal readonly record struct CenterEvent(
    long Version,
    CenterCommand Command,
    CenterOutcome Outcome);

internal readonly record struct CenterChange(
    CenterOutcome Outcome,
    CenterSnapshot Snapshot);

internal sealed class CenterSnapshot
{
    private CenterSnapshot(
        string centerRef,
        long version,
        ImmutableHashSet<string> departments,
        ImmutableDictionary<string, DoctorAffiliation> doctors,
        ImmutableDictionary<string, ImmutableHashSet<string>> doctorDepartments,
        ImmutableArray<CenterEvent> events)
    {
        CenterRef = centerRef;
        Version = version;
        Departments = departments;
        Doctors = doctors;
        DoctorDepartments = doctorDepartments;
        Events = events;
    }

    internal string CenterRef { get; }
    internal long Version { get; }
    internal ImmutableHashSet<string> Departments { get; }
    internal ImmutableDictionary<string, DoctorAffiliation> Doctors { get; }
    internal ImmutableDictionary<string, ImmutableHashSet<string>> DoctorDepartments { get; }
    internal ImmutableArray<CenterEvent> Events { get; }

    internal static CenterSnapshot NewSynthetic(string centerRef)
    {
        if (!ClinicalCenterJournal.IsSyntheticRef(centerRef))
            throw new ArgumentException("Synthetic center reference required.", nameof(centerRef));

        return new CenterSnapshot(
            centerRef,
            0,
            ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
            ImmutableDictionary<string, DoctorAffiliation>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableDictionary<string, ImmutableHashSet<string>>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableArray<CenterEvent>.Empty);
    }

    internal CenterSnapshot Append(
        CenterCommand command,
        ImmutableHashSet<string> departments,
        ImmutableDictionary<string, DoctorAffiliation> doctors,
        ImmutableDictionary<string, ImmutableHashSet<string>> assignments)
    {
        var next = checked(Version + 1);
        return new CenterSnapshot(CenterRef, next, departments, doctors, assignments,
            Events.Add(new CenterEvent(next, command, CenterOutcome.Applied)));
    }
}

internal static class ClinicalCenterJournal
{
    internal static bool IsSyntheticRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(x =>
            char.IsAsciiLetterOrDigit(x) || x == '-' || x == '_');

    internal static CenterChange Apply(CenterSnapshot? current, CenterCommand command)
    {
        if (current is null)
            throw new ArgumentNullException(nameof(current));

        if (command.RequestId == Guid.Empty || command.ExpectedVersion < 0 ||
            command.Kind is not (CenterCommandKind.DefineDepartment or
                CenterCommandKind.RecordDoctorAffiliation or
                CenterCommandKind.AssignDoctorToDepartment or
                CenterCommandKind.EndDoctorAffiliation or
                CenterCommandKind.UnassignDoctorFromDepartment) ||
            !IsSyntheticRef(command.TargetCenterRef) ||
            !IsSyntheticRef(command.ActingCenterRef) ||
            (command.DepartmentRef is not null && !IsSyntheticRef(command.DepartmentRef)) ||
            (command.DoctorRef is not null && !IsSyntheticRef(command.DoctorRef)))
            return new(CenterOutcome.Invalid, current);

        // A request ID is scoped to its ONE center event stream. Historical
        // exact replay after newer writes cannot rewind accepted state.
        foreach (var previous in current.Events)
        {
            if (previous.Command.RequestId != command.RequestId)
                continue;
            return previous.Command == command
                ? new(CenterOutcome.Replayed, current)
                : new(CenterOutcome.ConflictingReplay, current);
        }

        if (!string.Equals(command.TargetCenterRef, current.CenterRef, StringComparison.Ordinal) ||
            !string.Equals(command.ActingCenterRef, current.CenterRef, StringComparison.Ordinal))
            return new(CenterOutcome.ScopeDenied, current);

        if (command.CurrentCenterManagerAuthority == CenterEvidence.Rejected ||
            command.AuditPathAvailable == CenterEvidence.Rejected)
            return new(CenterOutcome.EvidenceDenied, current);

        if (command.CurrentCenterManagerAuthority != CenterEvidence.Confirmed ||
            command.AuditPathAvailable != CenterEvidence.Confirmed)
            return new(CenterOutcome.EvidenceMissing, current);

        if (command.ExpectedVersion != current.Version)
            return new(CenterOutcome.StaleRevision, current);

        if (current.Version == long.MaxValue)
            return new(CenterOutcome.Invalid, current);

        return command.Kind switch
        {
            CenterCommandKind.DefineDepartment => DefineDepartment(current, command),
            CenterCommandKind.RecordDoctorAffiliation => RecordDoctor(current, command),
            CenterCommandKind.AssignDoctorToDepartment => AssignDoctor(current, command),
            CenterCommandKind.EndDoctorAffiliation => EndDoctor(current, command),
            CenterCommandKind.UnassignDoctorFromDepartment => UnassignDoctor(current, command),
            _ => new(CenterOutcome.Invalid, current)
        };
    }

    private static CenterChange DefineDepartment(CenterSnapshot state, CenterCommand command)
    {
        if (command.DepartmentRef is null || command.DoctorRef is not null)
            return new(CenterOutcome.Invalid, state);
        if (state.Departments.Contains(command.DepartmentRef))
            return new(CenterOutcome.NotApplicable, state);

        return new(CenterOutcome.Applied, state.Append(
            command, state.Departments.Add(command.DepartmentRef),
            state.Doctors, state.DoctorDepartments));
    }

    private static CenterChange RecordDoctor(CenterSnapshot state, CenterCommand command)
    {
        if (command.DoctorRef is null || command.DepartmentRef is not null)
            return new(CenterOutcome.Invalid, state);

        // Ended relationships cannot be silently revived; that will need a
        // separately approved and evidenced reassociation contract.
        if (state.Doctors.ContainsKey(command.DoctorRef))
            return new(CenterOutcome.NotApplicable, state);

        return new(CenterOutcome.Applied, state.Append(command,
            state.Departments,
            state.Doctors.Add(command.DoctorRef, DoctorAffiliation.PendingIndependentVerification),
            state.DoctorDepartments));
    }

    private static CenterChange AssignDoctor(CenterSnapshot state, CenterCommand command)
    {
        if (command.DoctorRef is null || command.DepartmentRef is null)
            return new(CenterOutcome.Invalid, state);

        if (!state.Departments.Contains(command.DepartmentRef) ||
            !state.Doctors.TryGetValue(command.DoctorRef, out var status) ||
            status != DoctorAffiliation.PendingIndependentVerification)
            return new(CenterOutcome.NotApplicable, state);

        var assigned = state.DoctorDepartments.TryGetValue(command.DoctorRef, out var existing)
            ? existing
            : ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

        if (assigned.Contains(command.DepartmentRef))
            return new(CenterOutcome.NotApplicable, state);

        return new(CenterOutcome.Applied, state.Append(command,
            state.Departments, state.Doctors,
            state.DoctorDepartments.SetItem(command.DoctorRef,
                assigned.Add(command.DepartmentRef))));
    }

    // Remove only ONE test-only ward link; the unverified center-wide
    // doctor affiliation and every other existing ward remain unchanged.
    private static CenterChange UnassignDoctor(CenterSnapshot state, CenterCommand command)
    {
        if (command.DoctorRef is null || command.DepartmentRef is null)
            return new(CenterOutcome.Invalid, state);
        if (!state.Departments.Contains(command.DepartmentRef) ||
            !state.Doctors.TryGetValue(command.DoctorRef, out var status) ||
            status != DoctorAffiliation.PendingIndependentVerification ||
            !state.DoctorDepartments.TryGetValue(command.DoctorRef, out var existing) ||
            !existing.Contains(command.DepartmentRef))
            return new(CenterOutcome.NotApplicable, state);
        var remaining = existing.Remove(command.DepartmentRef);
        var assignments = remaining.Count == 0
            ? state.DoctorDepartments.Remove(command.DoctorRef)
            : state.DoctorDepartments.SetItem(command.DoctorRef, remaining);
        return new(CenterOutcome.Applied, state.Append(
            command, state.Departments, state.Doctors, assignments));
    }

    private static CenterChange EndDoctor(CenterSnapshot state, CenterCommand command)
    {
        if (command.DoctorRef is null || command.DepartmentRef is not null)
            return new(CenterOutcome.Invalid, state);

        if (!state.Doctors.TryGetValue(command.DoctorRef, out var status) ||
            status != DoctorAffiliation.PendingIndependentVerification)
            return new(CenterOutcome.NotApplicable, state);

        // Ended doctor keeps history but all current department associations
        // are removed. This never represents revocation of a real credential:
        // there is no real credential or runtime access in this domain.
        return new(CenterOutcome.Applied, state.Append(command,
            state.Departments,
            state.Doctors.SetItem(command.DoctorRef, DoctorAffiliation.Ended),
            state.DoctorDepartments.Remove(command.DoctorRef)));
    }
}
