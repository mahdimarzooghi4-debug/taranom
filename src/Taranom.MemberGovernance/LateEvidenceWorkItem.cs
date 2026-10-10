using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// Synthetic-only technical work item: NOT financial entitlement, identity,
// late-benefit approval, clinical consent, or authority to post to a wallet.
internal enum LateEvidenceAction
{
    SubmitUnverifiedEvidence = 1,
    RecordUnverifiedHumanNote = 2
}

internal enum LateEvidenceOutcome
{
    RecordedUnverified = 1,
    Replayed = 2,
    Invalid = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    SubmissionMissing = 6,
    ScopeMismatch = 7,
    SameClaimedActor = 8
}

// No amount, income-source assertion, award, approve/reject or entitlement flag.
// All refs MUST be test:* and remain unverified claims, not a real evidence store.
internal readonly record struct LateEvidenceCommand(
    Guid RequestId, long ExpectedRevision, LateEvidenceAction Action,
    string CaseRef, string MemberRef,
    DateTimeOffset PeriodStartInclusive, DateTimeOffset PeriodEndExclusive,
    string EvidenceRef, string SubmittedByClaimRef,
    Guid OriginalSubmissionRequestId,
    string? ReviewerClaimRef, string? ReviewNoteEvidenceRef);

internal readonly record struct LateEvidenceEntry(long Revision, LateEvidenceCommand Command);

internal sealed class LateEvidenceCaseSnapshot
{
    internal static LateEvidenceCaseSnapshot Empty { get; } =
        new(0, ImmutableArray<LateEvidenceEntry>.Empty);

    private LateEvidenceCaseSnapshot(long revision, ImmutableArray<LateEvidenceEntry> events)
    {
        Revision = revision;
        Events = events;
    }

    internal long Revision { get; }
    internal ImmutableArray<LateEvidenceEntry> Events { get; }

    internal LateEvidenceCaseSnapshot Append(LateEvidenceCommand command) =>
        new(checked(Revision + 1), Events.Add(new(Revision + 1, command)));
}

internal readonly record struct LateEvidenceChange(
    LateEvidenceOutcome Outcome, LateEvidenceCaseSnapshot Snapshot);

internal static class LateEvidenceWorkItem
{
    internal static LateEvidenceChange Apply(
        LateEvidenceCaseSnapshot? current, LateEvidenceCommand command)
    {
        var snapshot = current ?? LateEvidenceCaseSnapshot.Empty;
        if (current is null || command.RequestId == Guid.Empty ||
            command.ExpectedRevision < 0 || command.ExpectedRevision == long.MaxValue ||
            command.Action is not (LateEvidenceAction.SubmitUnverifiedEvidence
                or LateEvidenceAction.RecordUnverifiedHumanNote) ||
            !Synthetic(command.CaseRef) || !Synthetic(command.MemberRef) ||
            !Synthetic(command.EvidenceRef) || !Synthetic(command.SubmittedByClaimRef) ||
            command.PeriodStartInclusive >= command.PeriodEndExclusive)
            return new(LateEvidenceOutcome.Invalid, snapshot);

        if (command.Action == LateEvidenceAction.SubmitUnverifiedEvidence)
        {
            if (command.OriginalSubmissionRequestId != Guid.Empty ||
                command.ReviewerClaimRef is not null ||
                command.ReviewNoteEvidenceRef is not null)
                return new(LateEvidenceOutcome.Invalid, snapshot);
        }
        else if (command.OriginalSubmissionRequestId == Guid.Empty ||
            !Synthetic(command.ReviewerClaimRef) ||
            !Synthetic(command.ReviewNoteEvidenceRef))
            return new(LateEvidenceOutcome.Invalid, snapshot);

        // Historical exact replay precedes stale checks and never rewinds.
        foreach (var entry in snapshot.Events)
            if (entry.Command.RequestId == command.RequestId)
                return new(entry.Command == command
                    ? LateEvidenceOutcome.Replayed
                    : LateEvidenceOutcome.ConflictingReplay, snapshot);

        if (command.ExpectedRevision != snapshot.Revision)
            return new(LateEvidenceOutcome.StaleRevision, snapshot);

        if (snapshot.Events.IsEmpty)
            return command.Action == LateEvidenceAction.SubmitUnverifiedEvidence
                ? new(LateEvidenceOutcome.RecordedUnverified, snapshot.Append(command))
                : new(LateEvidenceOutcome.SubmissionMissing, snapshot);

        var original = snapshot.Events[0].Command;
        if (command.Action != LateEvidenceAction.RecordUnverifiedHumanNote)
            return new(LateEvidenceOutcome.ScopeMismatch, snapshot);

        // Pin review notes to EXACT submitted claim, not just a matching
        // arbitrary member or a loosely corresponding attribution date.
        if (command.CaseRef != original.CaseRef ||
            command.MemberRef != original.MemberRef ||
            command.PeriodStartInclusive != original.PeriodStartInclusive ||
            command.PeriodEndExclusive != original.PeriodEndExclusive ||
            command.EvidenceRef != original.EvidenceRef ||
            command.SubmittedByClaimRef != original.SubmittedByClaimRef ||
            command.OriginalSubmissionRequestId != original.RequestId)
            return new(LateEvidenceOutcome.ScopeMismatch, snapshot);

        // Distinct synthetic markers do not establish actual human identity
        // or reviewer independence. No signed judgement is made here.
        if (command.ReviewerClaimRef == command.SubmittedByClaimRef)
            return new(LateEvidenceOutcome.SameClaimedActor, snapshot);

        return new(LateEvidenceOutcome.RecordedUnverified, snapshot.Append(command));
    }

    private static bool Synthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
