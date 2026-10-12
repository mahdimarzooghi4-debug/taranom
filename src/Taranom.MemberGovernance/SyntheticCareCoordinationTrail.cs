using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// Only a test-only NONCLINICAL coordination trail. It cannot schedule care,
// authorize referrals, diagnose, apply care plans, or activate provider access.
internal enum SyntheticCareTrailAction
{
    RequestNonclinicalCoordination = 1,
    RecordUnverifiedCoordinatorContact = 2,
    AttachUnverifiedClinicalSourceReference = 3
}

internal enum SyntheticCareTrailOutcome
{
    RecordedUnverified = 1,
    Replayed = 2,
    Invalid = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    OriginalMissing = 6,
    ScopeMismatch = 7
}

internal readonly record struct SyntheticCareTrailCommand(
    Guid RequestId,
    long ExpectedRevision,
    SyntheticCareTrailAction Action,
    string CaseRef,
    string MemberRef,
    string OriginalMemberRequestRef,
    Guid OriginalSubmissionRequestId,
    string? ClaimedActorRef,
    string? UnverifiedNoteOrDocumentRef);

internal readonly record struct SyntheticCareTrailEntry(
    long Revision, SyntheticCareTrailCommand Command);

internal sealed class SyntheticCareTrailSnapshot
{
    internal static SyntheticCareTrailSnapshot Empty { get; } =
        new(0, ImmutableArray<SyntheticCareTrailEntry>.Empty);

    private SyntheticCareTrailSnapshot(long revision,
        ImmutableArray<SyntheticCareTrailEntry> history)
    {
        Revision = revision;
        History = history;
    }

    internal long Revision { get; }
    internal ImmutableArray<SyntheticCareTrailEntry> History { get; }

    internal SyntheticCareTrailSnapshot Append(SyntheticCareTrailCommand command) =>
        new(checked(Revision + 1),
            History.Add(new SyntheticCareTrailEntry(Revision + 1, command)));
}

internal readonly record struct SyntheticCareTrailChange(
    SyntheticCareTrailOutcome Outcome, SyntheticCareTrailSnapshot Snapshot);

// A claimed actor/ref is never a verified clinician licence, medical referral,
// legally authorized care instruction, clinical assessment, or delivered handoff.
// No state or action in this domain can enable clinical workflow execution.
internal static class SyntheticCareCoordinationTrail
{
    internal static SyntheticCareTrailChange Apply(
        SyntheticCareTrailSnapshot? current, SyntheticCareTrailCommand input)
    {
        var snapshot = current ?? SyntheticCareTrailSnapshot.Empty;
        if (current is null || input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 || input.ExpectedRevision == long.MaxValue ||
            input.Action is not
                (SyntheticCareTrailAction.RequestNonclinicalCoordination or
                 SyntheticCareTrailAction.RecordUnverifiedCoordinatorContact or
                 SyntheticCareTrailAction.AttachUnverifiedClinicalSourceReference) ||
            !Synthetic(input.CaseRef) || !Synthetic(input.MemberRef) ||
            !Synthetic(input.OriginalMemberRequestRef))
            return new(SyntheticCareTrailOutcome.Invalid, snapshot);

        if (input.Action == SyntheticCareTrailAction.RequestNonclinicalCoordination)
        {
            if (input.OriginalSubmissionRequestId != Guid.Empty ||
                input.ClaimedActorRef is not null ||
                input.UnverifiedNoteOrDocumentRef is not null)
                return new(SyntheticCareTrailOutcome.Invalid, snapshot);
        }
        else if (input.OriginalSubmissionRequestId == Guid.Empty ||
            !Synthetic(input.ClaimedActorRef) ||
            !Synthetic(input.UnverifiedNoteOrDocumentRef))
            return new(SyntheticCareTrailOutcome.Invalid, snapshot);

        // Exactly identical replay is safe after new notes; any changed
        // request payload is a conflict, never an overwrite.
        foreach (var historical in snapshot.History)
        {
            if (historical.Command.RequestId != input.RequestId) continue;
            return new(historical.Command == input
                ? SyntheticCareTrailOutcome.Replayed
                : SyntheticCareTrailOutcome.ConflictingReplay, snapshot);
        }

        if (input.ExpectedRevision != snapshot.Revision)
            return new(SyntheticCareTrailOutcome.StaleRevision, snapshot);

        if (snapshot.History.IsEmpty)
            return input.Action == SyntheticCareTrailAction.RequestNonclinicalCoordination
                ? new(SyntheticCareTrailOutcome.RecordedUnverified, snapshot.Append(input))
                : new(SyntheticCareTrailOutcome.OriginalMissing, snapshot);

        var origin = snapshot.History[0].Command;
        if (input.Action == SyntheticCareTrailAction.RequestNonclinicalCoordination ||
            input.OriginalSubmissionRequestId != origin.RequestId ||
            input.CaseRef != origin.CaseRef ||
            input.MemberRef != origin.MemberRef ||
            input.OriginalMemberRequestRef != origin.OriginalMemberRequestRef)
            return new(SyntheticCareTrailOutcome.ScopeMismatch, snapshot);

        return new(SyntheticCareTrailOutcome.RecordedUnverified, snapshot.Append(input));
    }

    private static bool Synthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}

internal readonly record struct SyntheticCareTrailRead(
    string CaseRef, string MemberRef, long Revision,
    int UnverifiedCoordinatorContactCount, int UnverifiedClinicalReferenceCount);

// Only count historical claims; not a status, referral, authorization,
// fulfilled visit, care quality assessment, or clinical validity assertion.
internal static class SyntheticCareTrailProjection
{
    internal static SyntheticCareTrailRead? Project(SyntheticCareTrailSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.History.IsEmpty) return null;
        var first = snapshot.History[0].Command;
        return new(first.CaseRef, first.MemberRef, snapshot.Revision,
            snapshot.History.Count(x => x.Command.Action ==
                SyntheticCareTrailAction.RecordUnverifiedCoordinatorContact),
            snapshot.History.Count(x => x.Command.Action ==
                SyntheticCareTrailAction.AttachUnverifiedClinicalSourceReference));
    }
}
