using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// Synthetic only: a support-case reference does NOT authenticate a member,
// grant access to clinical records, reach a real insurer or create a legal case.
internal enum SyntheticSupportTopic
{
    MedicalServiceComplaint = 1,
    InsuranceCoverageDispute = 2
}

internal enum SyntheticSupportAction
{
    SubmitUnverified = 1,
    RecordUnverifiedFollowUp = 2
}

internal enum SyntheticSupportOutcome
{
    RecordedUnverified = 1,
    Replayed = 2,
    Invalid = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    OriginalMissing = 6,
    ScopeMismatch = 7
}

internal readonly record struct SyntheticSupportCommand(
    Guid RequestId,
    long ExpectedRevision,
    SyntheticSupportAction Action,
    string CaseRef,
    string MemberRef,
    SyntheticSupportTopic Topic,
    string UnverifiedEvidenceRef,
    string ClaimedSubmitterRef,
    Guid OriginalSubmissionRequestId,
    string? ClaimedFollowUpActorRef,
    string? UnverifiedFollowUpNoteRef);

internal readonly record struct SyntheticSupportEntry(
    long Revision, SyntheticSupportCommand Command);

internal sealed class SyntheticSupportSnapshot
{
    internal static SyntheticSupportSnapshot Empty { get; } =
        new(0, ImmutableArray<SyntheticSupportEntry>.Empty);

    private SyntheticSupportSnapshot(
        long revision, ImmutableArray<SyntheticSupportEntry> history)
    {
        Revision = revision;
        History = history;
    }

    internal long Revision { get; }
    internal ImmutableArray<SyntheticSupportEntry> History { get; }

    internal SyntheticSupportSnapshot Append(SyntheticSupportCommand input) =>
        new(checked(Revision + 1),
            History.Add(new SyntheticSupportEntry(Revision + 1, input)));
}

internal readonly record struct SyntheticSupportChange(
    SyntheticSupportOutcome Outcome, SyntheticSupportSnapshot Snapshot);

// This contract intentionally has NO status such as Assigned, Referred,
// InsurerApproved, ClinicallyResolved, Liable, Reimbursed or Paid.
// An appended note is not proof a real provider/insurer was contacted.
internal static class SyntheticSupportIntake
{
    internal static SyntheticSupportChange Apply(
        SyntheticSupportSnapshot? current, SyntheticSupportCommand input)
    {
        var snapshot = current ?? SyntheticSupportSnapshot.Empty;
        if (current is null || input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 || input.ExpectedRevision == long.MaxValue ||
            input.Action is not (SyntheticSupportAction.SubmitUnverified or
                SyntheticSupportAction.RecordUnverifiedFollowUp) ||
            input.Topic is not (SyntheticSupportTopic.MedicalServiceComplaint or
                SyntheticSupportTopic.InsuranceCoverageDispute) ||
            !Synthetic(input.CaseRef) || !Synthetic(input.MemberRef) ||
            !Synthetic(input.UnverifiedEvidenceRef) ||
            !Synthetic(input.ClaimedSubmitterRef))
            return new(SyntheticSupportOutcome.Invalid, snapshot);

        if (input.Action == SyntheticSupportAction.SubmitUnverified)
        {
            if (input.OriginalSubmissionRequestId != Guid.Empty ||
                input.ClaimedFollowUpActorRef is not null ||
                input.UnverifiedFollowUpNoteRef is not null)
                return new(SyntheticSupportOutcome.Invalid, snapshot);
        }
        else if (input.OriginalSubmissionRequestId == Guid.Empty ||
            !Synthetic(input.ClaimedFollowUpActorRef) ||
            !Synthetic(input.UnverifiedFollowUpNoteRef))
            return new(SyntheticSupportOutcome.Invalid, snapshot);

        // An exact replay can be recognized after newer notes without
        // resurrecting an earlier snapshot; changed request ID payload fails.
        foreach (var entry in snapshot.History)
        {
            if (entry.Command.RequestId != input.RequestId) continue;
            return new(entry.Command == input
                ? SyntheticSupportOutcome.Replayed
                : SyntheticSupportOutcome.ConflictingReplay, snapshot);
        }

        if (input.ExpectedRevision != snapshot.Revision)
            return new(SyntheticSupportOutcome.StaleRevision, snapshot);

        if (snapshot.History.IsEmpty)
        {
            return input.Action == SyntheticSupportAction.SubmitUnverified
                ? new(SyntheticSupportOutcome.RecordedUnverified, snapshot.Append(input))
                : new(SyntheticSupportOutcome.OriginalMissing, snapshot);
        }

        // Only follow-up notes can append; no resubmission, topic change,
        // cross-member contamination or reassignment of legal authority.
        var submission = snapshot.History[0].Command;
        if (input.Action != SyntheticSupportAction.RecordUnverifiedFollowUp ||
            input.OriginalSubmissionRequestId != submission.RequestId ||
            input.CaseRef != submission.CaseRef ||
            input.MemberRef != submission.MemberRef ||
            input.Topic != submission.Topic ||
            input.UnverifiedEvidenceRef != submission.UnverifiedEvidenceRef ||
            input.ClaimedSubmitterRef != submission.ClaimedSubmitterRef)
            return new(SyntheticSupportOutcome.ScopeMismatch, snapshot);

        return new(SyntheticSupportOutcome.RecordedUnverified, snapshot.Append(input));
    }

    private static bool Synthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}

// Read projection never claims an authoritative case lifecycle or outcome.
// It only exposes the purely synthetic historical intake and note counts.
internal readonly record struct SyntheticSupportRead(
    SyntheticSupportTopic Topic, string CaseRef, string MemberRef,
    long Revision, int UnverifiedNoteCount,
    bool OriginalSubmissionPresent);

internal static class SyntheticSupportReadProjection
{
    internal static SyntheticSupportRead? Project(SyntheticSupportSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.History.IsEmpty) return null;
        var first = snapshot.History[0].Command;
        return new(first.Topic, first.CaseRef, first.MemberRef,
            snapshot.Revision, snapshot.History.Length - 1, true);
    }
}
