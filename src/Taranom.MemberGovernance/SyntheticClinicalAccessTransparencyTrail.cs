using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// TRN-BIZ-029/035/036/041: INTERNAL synthetic-only paper trace.
// No real clinical record, medical permission, identity, consent or audit.
internal enum SyntheticAccessNoteAction
{
    OpenUnverifiedClinicianRequest = 1,
    RecordUnverifiedMemberConcern = 2,
    RecordUnverifiedOversightNote = 3
}

internal enum SyntheticAccessNoteOutcome
{
    Invalid = 1,
    RecordedUnverified = 2,
    Replayed = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    OriginalMissing = 6,
    ScopeChanged = 7,
    DuplicateStage = 8
}

internal enum SyntheticAccessTransparencyState
{
    Unavailable = 1,
    ViewerDenied = 2,
    ViewerScopeMismatch = 3,
    UnverifiedPaperOnly = 4
}

internal readonly record struct SyntheticAccessNoteCommand(
    Guid RequestId,
    long ExpectedRevision,
    SyntheticAccessNoteAction Action,
    SyntheticRecordDisclosureInput ClaimedDisclosureScope,
    Guid OriginalRequestId,
    string? ClaimedActorRef,
    string? UnverifiedEvidenceRef);

internal readonly record struct SyntheticAccessNoteEntry(
    long Revision, SyntheticAccessNoteCommand Command);

internal sealed class SyntheticAccessTransparencySnapshot
{
    internal static SyntheticAccessTransparencySnapshot Empty { get; } =
        new(0, ImmutableArray<SyntheticAccessNoteEntry>.Empty);

    private SyntheticAccessTransparencySnapshot(
        long revision, ImmutableArray<SyntheticAccessNoteEntry> history)
    {
        Revision = revision;
        History = history;
    }

    internal long Revision { get; }
    internal ImmutableArray<SyntheticAccessNoteEntry> History { get; }

    internal SyntheticAccessTransparencySnapshot Append(
        SyntheticAccessNoteCommand command) =>
        new(checked(Revision + 1),
            History.Add(new(Revision + 1, command)));
}

internal readonly record struct SyntheticAccessNoteChange(
    SyntheticAccessNoteOutcome Outcome, SyntheticAccessTransparencySnapshot Snapshot);

internal readonly record struct SyntheticAccessTransparencyRead(
    SyntheticAccessTransparencyState State,
    long Revision,
    int UnverifiedRequestCount,
    int UnverifiedMemberConcernCount,
    int UnverifiedOversightNoteCount,
    SyntheticRecordDisclosureResult? UntrustedPrerequisiteAssessment,
    bool RecordReadOccurred,
    bool DisclosureGranted,
    bool LegalExceptionEstablished);

// All inputs are caller-controlled test-only claims. Reuse the existing
// treatment-purpose gate; even PreconditionsPresentOnly is NOT a real read.
internal static class SyntheticClinicalAccessTransparencyTrail
{
    internal static SyntheticAccessNoteChange Apply(
        SyntheticAccessTransparencySnapshot? current,
        SyntheticAccessNoteCommand command)
    {
        var snapshot = current ?? SyntheticAccessTransparencySnapshot.Empty;
        if (current is null || command.RequestId == Guid.Empty ||
            command.ExpectedRevision < 0 ||
            command.ExpectedRevision == long.MaxValue ||
            command.Action is not (
                SyntheticAccessNoteAction.OpenUnverifiedClinicianRequest or
                SyntheticAccessNoteAction.RecordUnverifiedMemberConcern or
                SyntheticAccessNoteAction.RecordUnverifiedOversightNote))
            return new(SyntheticAccessNoteOutcome.Invalid, snapshot);

        var scope = command.ClaimedDisclosureScope;
        if (scope.Purpose != SyntheticRecordPurpose.ClinicianTreatmentNeed ||
            scope.RequesterKind != SyntheticRecordRequester.Clinician)
            return new(SyntheticAccessNoteOutcome.Invalid, snapshot);

        var assessment = SyntheticClinicalRecordDisclosureGate.Assess(scope);
        if (assessment is SyntheticRecordDisclosureResult.InvalidInput or
            SyntheticRecordDisclosureResult.ScopeMismatch or
            SyntheticRecordDisclosureResult.RequesterDenied or
            SyntheticRecordDisclosureResult.SeparateMemberSelfReadRequired or
            SyntheticRecordDisclosureResult.SeparateResearchAuthorizationRequired or
            SyntheticRecordDisclosureResult.EmergencyExceptionNotEstablished)
            return new(SyntheticAccessNoteOutcome.Invalid, snapshot);

        if (command.Action == SyntheticAccessNoteAction.OpenUnverifiedClinicianRequest)
        {
            if (command.OriginalRequestId != Guid.Empty ||
                command.ClaimedActorRef is not null ||
                command.UnverifiedEvidenceRef is not null)
                return new(SyntheticAccessNoteOutcome.Invalid, snapshot);
        }
        else if (command.OriginalRequestId == Guid.Empty ||
                 !Test(command.ClaimedActorRef) ||
                 !Test(command.UnverifiedEvidenceRef))
            return new(SyntheticAccessNoteOutcome.Invalid, snapshot);
        else if (command.Action == SyntheticAccessNoteAction.RecordUnverifiedMemberConcern
                 ? command.ClaimedActorRef != scope.MemberRef
                 : command.ClaimedActorRef == scope.MemberRef ||
                   command.ClaimedActorRef == scope.ClaimedClinicianRef)
            return new(SyntheticAccessNoteOutcome.Invalid, snapshot);

        // A previously accepted exact UUID+payload is an idempotent read,
        // even after additional paper notes. Changing any source or actor
        // under the same UUID is a conflict rather than a correction.
        foreach (var entry in snapshot.History)
        {
            if (entry.Command.RequestId != command.RequestId) continue;
            return new(entry.Command == command
                ? SyntheticAccessNoteOutcome.Replayed
                : SyntheticAccessNoteOutcome.ConflictingReplay, snapshot);
        }

        if (command.ExpectedRevision != snapshot.Revision)
            return new(SyntheticAccessNoteOutcome.StaleRevision, snapshot);

        if (snapshot.History.IsEmpty)
            return command.Action == SyntheticAccessNoteAction.OpenUnverifiedClinicianRequest
                ? new(SyntheticAccessNoteOutcome.RecordedUnverified, snapshot.Append(command))
                : new(SyntheticAccessNoteOutcome.OriginalMissing, snapshot);

        var original = snapshot.History[0].Command;
        if (command.Action == SyntheticAccessNoteAction.OpenUnverifiedClinicianRequest ||
            command.OriginalRequestId != original.RequestId ||
            command.ClaimedDisclosureScope != original.ClaimedDisclosureScope)
            return new(SyntheticAccessNoteOutcome.ScopeChanged, snapshot);

        if (snapshot.History.Any(entry => entry.Command.Action == command.Action))
            return new(SyntheticAccessNoteOutcome.DuplicateStage, snapshot);

        // A claimed concern does not revoke actual access (there is none);
        // a claimed oversight note is neither a decision nor incident close.
        return new(SyntheticAccessNoteOutcome.RecordedUnverified, snapshot.Append(command));
    }

    internal static SyntheticAccessTransparencyRead ProjectForClaimedMember(
        SyntheticAccessTransparencySnapshot? snapshot,
        SelfReadBoundaryInput viewer)
    {
        static SyntheticAccessTransparencyRead Deny(
            SyntheticAccessTransparencyState status) =>
            new(status, 0, 0, 0, 0, null, false, false, false);

        if (snapshot is null || snapshot.History.IsEmpty)
            return Deny(SyntheticAccessTransparencyState.Unavailable);

        var root = snapshot.History[0].Command.ClaimedDisclosureScope;
        if (viewer.ActorKind != SelfReadActorKind.Member)
            return Deny(SyntheticAccessTransparencyState.ViewerDenied);

        if (viewer.ResourceOwnerMemberRef != root.MemberRef ||
            viewer.ResourceOwnerTenantRef != root.TenantRef ||
            viewer.ServerResolvedMemberRef != root.MemberRef ||
            viewer.ServerResolvedTenantRef != root.TenantRef)
            return Deny(SyntheticAccessTransparencyState.ViewerScopeMismatch);

        // Read prerequisite checks are explicitly separate from treatment
        // disclosure assessment, including independent legal/session basis.
        if (MemberSelfReadBoundary.Assess(viewer) !=
            SelfReadBoundaryResult.PreconditionsPresentOnly)
            return Deny(SyntheticAccessTransparencyState.ViewerDenied);

        var assessment = SyntheticClinicalRecordDisclosureGate.Assess(root);
        if (assessment is SyntheticRecordDisclosureResult.InvalidInput or
            SyntheticRecordDisclosureResult.ScopeMismatch)
            return Deny(SyntheticAccessTransparencyState.Unavailable);

        return new(SyntheticAccessTransparencyState.UnverifiedPaperOnly,
            snapshot.Revision, 1,
            snapshot.History.Count(e => e.Command.Action ==
                SyntheticAccessNoteAction.RecordUnverifiedMemberConcern),
            snapshot.History.Count(e => e.Command.Action ==
                SyntheticAccessNoteAction.RecordUnverifiedOversightNote),
            assessment, false, false, false);
    }

    private static bool Test(string? v) =>
        v is { Length: > 5 and <= 128 } &&
        v.StartsWith("test:", StringComparison.Ordinal) &&
        v.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');
}
