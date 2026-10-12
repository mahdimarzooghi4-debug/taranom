using System.Collections.Immutable;

namespace Taranom.ClinicalCenterGovernance;

// TRN-BIZ-043/044/045: nonoperational, pure INTERNAL synthetic-only trace.
// Do not interpret any paper note as provider qualification, formal network
// oversight, real incident notification, professional finding or suspension.
internal enum SyntheticProviderQualityNoteAction
{
    RecordUnverifiedQualityScope = 1,
    RecordUnverifiedTaranomStandardNote = 2,
    RecordUnverifiedNetworkProviderControlNote = 3,
    RecordUnverifiedIncidentHandoffReference = 4
}

internal enum SyntheticProviderQualityNoteOutcome
{
    Invalid = 1,
    RecordedUnverified = 2,
    Replayed = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    OriginalScopeMissing = 6,
    ScopeMismatch = 7,
    NetworkScopeDenied = 8
}

// Every ID is a synthetic opaque 'test:' ref, NOT a provider licence,
// incident record, actual medical data, trusted actor or external consent.
internal readonly record struct SyntheticProviderQualityNote(
    Guid RequestId,
    long ExpectedRevision,
    SyntheticProviderQualityNoteAction Action,
    string? CaseRef,
    string? TenantRef,
    string? ProviderRef,
    string? ServiceRef,
    SyntheticProviderPath Path,
    string? PartnerNetworkRef,
    Guid OriginalScopeRequestId,
    string? UnverifiedEvidenceRef,
    string? ClaimedActorRef,
    string? UnverifiedIncidentRef,
    string? ClaimedReceivingAuthorityRef);

internal readonly record struct SyntheticProviderQualityEntry(
    long Revision, SyntheticProviderQualityNote Note);

internal sealed class SyntheticProviderQualitySnapshot
{
    internal static SyntheticProviderQualitySnapshot Empty { get; } =
        new(0, ImmutableArray<SyntheticProviderQualityEntry>.Empty);

    private SyntheticProviderQualitySnapshot(
        long revision, ImmutableArray<SyntheticProviderQualityEntry> events)
    {
        Revision = revision;
        Events = events;
    }

    internal long Revision { get; }
    internal ImmutableArray<SyntheticProviderQualityEntry> Events { get; }

    internal SyntheticProviderQualitySnapshot Append(SyntheticProviderQualityNote note) =>
        new(checked(Revision + 1),
            Events.Add(new(Revision + 1, note)));
}

internal readonly record struct SyntheticProviderQualityChange(
    SyntheticProviderQualityNoteOutcome Outcome,
    SyntheticProviderQualitySnapshot Snapshot);

// This does not invoke SyntheticProviderNetworkEvidenceGate as an authorization
// shortcut. Qualification and any actual clinical permission remain separate.
internal static class SyntheticProviderQualityTrail
{
    internal static SyntheticProviderQualityChange Apply(
        SyntheticProviderQualitySnapshot? current, SyntheticProviderQualityNote input)
    {
        var existing = current ?? SyntheticProviderQualitySnapshot.Empty;
        if (current is null || input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 || input.ExpectedRevision == long.MaxValue ||
            !Enum.IsDefined(input.Action) ||
            !ClinicalCenterJournal.IsSyntheticRef(input.CaseRef) ||
            !ClinicalCenterJournal.IsSyntheticRef(input.TenantRef) ||
            !ClinicalCenterJournal.IsSyntheticRef(input.ProviderRef) ||
            !ClinicalCenterJournal.IsSyntheticRef(input.ServiceRef) ||
            input.Path is not (
                SyntheticProviderPath.DirectContract or
                SyntheticProviderPath.QualifiedPartnerNetwork))
            return new(SyntheticProviderQualityNoteOutcome.Invalid, existing);

        if (input.Path == SyntheticProviderPath.DirectContract)
        {
            if (input.PartnerNetworkRef is not null)
                return new(SyntheticProviderQualityNoteOutcome.NetworkScopeDenied, existing);
        }
        else if (!ClinicalCenterJournal.IsSyntheticRef(input.PartnerNetworkRef))
            return new(SyntheticProviderQualityNoteOutcome.NetworkScopeDenied, existing);

        if (input.Action == SyntheticProviderQualityNoteAction.RecordUnverifiedQualityScope)
        {
            if (input.OriginalScopeRequestId != Guid.Empty ||
                input.UnverifiedEvidenceRef is not null ||
                input.ClaimedActorRef is not null ||
                input.UnverifiedIncidentRef is not null ||
                input.ClaimedReceivingAuthorityRef is not null)
                return new(SyntheticProviderQualityNoteOutcome.Invalid, existing);
        }
        else
        {
            if (input.OriginalScopeRequestId == Guid.Empty ||
                !ClinicalCenterJournal.IsSyntheticRef(input.UnverifiedEvidenceRef) ||
                !ClinicalCenterJournal.IsSyntheticRef(input.ClaimedActorRef))
                return new(SyntheticProviderQualityNoteOutcome.Invalid, existing);

            if (input.Action ==
                SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference)
            {
                if (!ClinicalCenterJournal.IsSyntheticRef(input.UnverifiedIncidentRef) ||
                    !ClinicalCenterJournal.IsSyntheticRef(input.ClaimedReceivingAuthorityRef))
                    return new(SyntheticProviderQualityNoteOutcome.Invalid, existing);
            }
            else if (input.UnverifiedIncidentRef is not null ||
                     input.ClaimedReceivingAuthorityRef is not null)
                return new(SyntheticProviderQualityNoteOutcome.Invalid, existing);

            if (input.Action ==
                    SyntheticProviderQualityNoteAction.RecordUnverifiedNetworkProviderControlNote &&
                input.Path != SyntheticProviderPath.QualifiedPartnerNetwork)
                return new(SyntheticProviderQualityNoteOutcome.NetworkScopeDenied, existing);
        }

        // A request ID replay is scoped to the entire immutable input, and
        // checked before CAS: replaying old requests cannot rewrite history.
        foreach (var prior in existing.Events)
        {
            if (prior.Note.RequestId != input.RequestId) continue;
            return new(prior.Note == input
                ? SyntheticProviderQualityNoteOutcome.Replayed
                : SyntheticProviderQualityNoteOutcome.ConflictingReplay, existing);
        }
        if (input.ExpectedRevision != existing.Revision)
            return new(SyntheticProviderQualityNoteOutcome.StaleRevision, existing);

        if (existing.Events.IsEmpty)
            return input.Action ==
                SyntheticProviderQualityNoteAction.RecordUnverifiedQualityScope
                ? new(SyntheticProviderQualityNoteOutcome.RecordedUnverified,
                    existing.Append(input))
                : new(SyntheticProviderQualityNoteOutcome.OriginalScopeMissing, existing);

        var origin = existing.Events[0].Note;
        if (input.Action == SyntheticProviderQualityNoteAction.RecordUnverifiedQualityScope ||
            input.OriginalScopeRequestId != origin.RequestId ||
            input.CaseRef != origin.CaseRef ||
            input.TenantRef != origin.TenantRef ||
            input.ProviderRef != origin.ProviderRef ||
            input.ServiceRef != origin.ServiceRef ||
            input.Path != origin.Path ||
            input.PartnerNetworkRef != origin.PartnerNetworkRef)
            return new(SyntheticProviderQualityNoteOutcome.ScopeMismatch, existing);

        return new(SyntheticProviderQualityNoteOutcome.RecordedUnverified,
            existing.Append(input));
    }
}

// Only counts of unverified notes. Not ratings, licences, sanctions, actual
// referrals or complaint resolution. The member complaint intake is separate.
internal readonly record struct SyntheticProviderQualityRead(
    string CaseRef, string TenantRef, string ProviderRef, string ServiceRef,
    SyntheticProviderPath Path, long Revision,
    int ClaimedTaranomStandardNoteCount,
    int ClaimedNetworkControlNoteCount,
    int ClaimedIncidentHandoffReferenceCount);

internal static class SyntheticProviderQualityProjection
{
    internal static SyntheticProviderQualityRead? Project(
        SyntheticProviderQualitySnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Events.IsEmpty)
            return null;
        var origin = snapshot.Events[0].Note;
        return new(origin.CaseRef!, origin.TenantRef!, origin.ProviderRef!,
            origin.ServiceRef!, origin.Path, snapshot.Revision,
            snapshot.Events.Count(e => e.Note.Action ==
                SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote),
            snapshot.Events.Count(e => e.Note.Action ==
                SyntheticProviderQualityNoteAction.RecordUnverifiedNetworkProviderControlNote),
            snapshot.Events.Count(e => e.Note.Action ==
                SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference));
    }
}
