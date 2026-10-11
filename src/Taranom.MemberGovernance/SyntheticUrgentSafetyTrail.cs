using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// TRN-BIZ-033/034/035: INTERNAL, PURE, SYNTHETIC-ONLY paper lineage.
// No symptom classification, emergency dispatch, actual delivered warning,
// trusted clinician receipt, patient record or clinical decision.
internal enum SyntheticUrgentSafetyAction
{
    OpenUnverifiedConcernContext = 1,
    RecordUnverifiedWarningEvidence = 2,
    RecordUnverifiedReferralRequestEvidence = 3,
    RecordUnverifiedClinicalReceiptEvidence = 4
}

internal enum SyntheticUrgentSafetyOutcome
{
    Invalid = 1,
    RecordedUnverified = 2,
    Replayed = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    OriginalContextMissing = 6,
    ScopeMismatch = 7,
    ReferralRequestMissing = 8,
    ClaimedRecipientMismatch = 9,
    DuplicatePaperStage = 10
}

internal readonly record struct SyntheticUrgentSafetyCommand(
    Guid RequestId,
    long ExpectedRevision,
    SyntheticUrgentSafetyAction Action,
    string? CaseRef,
    string? MemberRef,
    string? UnverifiedConcernRef,
    Guid OriginalContextRequestId,
    string? ClaimedActorRef,
    string? UnverifiedEvidenceRef,
    Guid OriginalReferralRequestId,
    string? ClaimedClinicalTeamRef,
    string? UnverifiedTeamAuthorityEvidenceRef);

internal readonly record struct SyntheticUrgentSafetyEvent(
    long Revision, SyntheticUrgentSafetyCommand Command);

internal sealed class SyntheticUrgentSafetySnapshot
{
    internal static SyntheticUrgentSafetySnapshot Empty { get; } =
        new(0, ImmutableArray<SyntheticUrgentSafetyEvent>.Empty);

    private SyntheticUrgentSafetySnapshot(
        long revision, ImmutableArray<SyntheticUrgentSafetyEvent> events)
    {
        Revision = revision;
        Events = events;
    }

    internal long Revision { get; }
    internal ImmutableArray<SyntheticUrgentSafetyEvent> Events { get; }

    internal SyntheticUrgentSafetySnapshot Append(SyntheticUrgentSafetyCommand command) =>
        new(checked(Revision + 1),
            Events.Add(new(Revision + 1, command)));
}

internal readonly record struct SyntheticUrgentSafetyChange(
    SyntheticUrgentSafetyOutcome Outcome, SyntheticUrgentSafetySnapshot Snapshot);

// Stages are deliberately nonoperational. Warning-design evidence and
// proposed clinician referral are INDEPENDENT: one may be recorded before
// the other. Receipt is a separate claimed paper reference, never inferred
// from request, and cannot certify a real clinician or live service.
internal static class SyntheticUrgentSafetyTrail
{
    internal static SyntheticUrgentSafetyChange Apply(
        SyntheticUrgentSafetySnapshot? current,
        SyntheticUrgentSafetyCommand input)
    {
        var snapshot = current ?? SyntheticUrgentSafetySnapshot.Empty;
        if (current is null || input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 || input.ExpectedRevision == long.MaxValue ||
            input.Action is not (
                SyntheticUrgentSafetyAction.OpenUnverifiedConcernContext or
                SyntheticUrgentSafetyAction.RecordUnverifiedWarningEvidence or
                SyntheticUrgentSafetyAction.RecordUnverifiedReferralRequestEvidence or
                SyntheticUrgentSafetyAction.RecordUnverifiedClinicalReceiptEvidence) ||
            !Synthetic(input.CaseRef) || !Synthetic(input.MemberRef) ||
            !Synthetic(input.UnverifiedConcernRef))
            return new(SyntheticUrgentSafetyOutcome.Invalid, snapshot);

        if (input.Action == SyntheticUrgentSafetyAction.OpenUnverifiedConcernContext)
        {
            if (input.OriginalContextRequestId != Guid.Empty ||
                input.ClaimedActorRef is not null ||
                input.UnverifiedEvidenceRef is not null ||
                input.OriginalReferralRequestId != Guid.Empty ||
                input.ClaimedClinicalTeamRef is not null ||
                input.UnverifiedTeamAuthorityEvidenceRef is not null)
                return new(SyntheticUrgentSafetyOutcome.Invalid, snapshot);
        }
        else
        {
            if (input.OriginalContextRequestId == Guid.Empty ||
                !Synthetic(input.ClaimedActorRef) ||
                !Synthetic(input.UnverifiedEvidenceRef))
                return new(SyntheticUrgentSafetyOutcome.Invalid, snapshot);

            if (input.Action == SyntheticUrgentSafetyAction.RecordUnverifiedWarningEvidence)
            {
                if (input.OriginalReferralRequestId != Guid.Empty ||
                    input.ClaimedClinicalTeamRef is not null ||
                    input.UnverifiedTeamAuthorityEvidenceRef is not null)
                    return new(SyntheticUrgentSafetyOutcome.Invalid, snapshot);
            }
            else
            {
                if (!Synthetic(input.ClaimedClinicalTeamRef) ||
                    !Synthetic(input.UnverifiedTeamAuthorityEvidenceRef))
                    return new(SyntheticUrgentSafetyOutcome.Invalid, snapshot);

                if (input.Action ==
                    SyntheticUrgentSafetyAction.RecordUnverifiedReferralRequestEvidence &&
                    input.OriginalReferralRequestId != Guid.Empty)
                    return new(SyntheticUrgentSafetyOutcome.Invalid, snapshot);

                if (input.Action ==
                    SyntheticUrgentSafetyAction.RecordUnverifiedClinicalReceiptEvidence &&
                    input.OriginalReferralRequestId == Guid.Empty)
                    return new(SyntheticUrgentSafetyOutcome.Invalid, snapshot);
            }
        }

        // Check replay against exact immutable payload before expected
        // revision, including actor, claimed team and evidence sources.
        foreach (var evt in snapshot.Events)
        {
            if (evt.Command.RequestId != input.RequestId) continue;
            return new(evt.Command == input
                ? SyntheticUrgentSafetyOutcome.Replayed
                : SyntheticUrgentSafetyOutcome.ConflictingReplay, snapshot);
        }

        if (input.ExpectedRevision != snapshot.Revision)
            return new(SyntheticUrgentSafetyOutcome.StaleRevision, snapshot);

        if (snapshot.Events.IsEmpty)
            return input.Action == SyntheticUrgentSafetyAction.OpenUnverifiedConcernContext
                ? new(SyntheticUrgentSafetyOutcome.RecordedUnverified,
                    snapshot.Append(input))
                : new(SyntheticUrgentSafetyOutcome.OriginalContextMissing, snapshot);

        var original = snapshot.Events[0].Command;
        if (input.Action == SyntheticUrgentSafetyAction.OpenUnverifiedConcernContext ||
            input.OriginalContextRequestId != original.RequestId ||
            input.CaseRef != original.CaseRef ||
            input.MemberRef != original.MemberRef ||
            input.UnverifiedConcernRef != original.UnverifiedConcernRef)
            return new(SyntheticUrgentSafetyOutcome.ScopeMismatch, snapshot);

        if (input.Action == SyntheticUrgentSafetyAction.RecordUnverifiedWarningEvidence)
        {
            if (snapshot.Events.Any(x => x.Command.Action ==
                SyntheticUrgentSafetyAction.RecordUnverifiedWarningEvidence))
                return new(SyntheticUrgentSafetyOutcome.DuplicatePaperStage, snapshot);
            return new(SyntheticUrgentSafetyOutcome.RecordedUnverified, snapshot.Append(input));
        }

        if (input.Action == SyntheticUrgentSafetyAction.RecordUnverifiedReferralRequestEvidence)
        {
            if (snapshot.Events.Any(x => x.Command.Action ==
                SyntheticUrgentSafetyAction.RecordUnverifiedReferralRequestEvidence))
                return new(SyntheticUrgentSafetyOutcome.DuplicatePaperStage, snapshot);
            // No need for an earlier warning PAPER note to record a proposed
            // referral, and referral never gates generic safety guidance.
            return new(SyntheticUrgentSafetyOutcome.RecordedUnverified, snapshot.Append(input));
        }

        var request = snapshot.Events.FirstOrDefault(x =>
            x.Command.Action ==
                SyntheticUrgentSafetyAction.RecordUnverifiedReferralRequestEvidence &&
            x.Command.RequestId == input.OriginalReferralRequestId);
        if (request.Command.RequestId == Guid.Empty)
            return new(SyntheticUrgentSafetyOutcome.ReferralRequestMissing, snapshot);

        // A receipt claim must be associated with EXACT original proposed
        // destination and purported authority; changed recipient fails.
        if (input.ClaimedClinicalTeamRef != request.Command.ClaimedClinicalTeamRef ||
            input.UnverifiedTeamAuthorityEvidenceRef !=
                request.Command.UnverifiedTeamAuthorityEvidenceRef ||
            input.ClaimedActorRef == request.Command.ClaimedActorRef)
            return new(SyntheticUrgentSafetyOutcome.ClaimedRecipientMismatch, snapshot);

        if (snapshot.Events.Any(x => x.Command.Action ==
            SyntheticUrgentSafetyAction.RecordUnverifiedClinicalReceiptEvidence))
            return new(SyntheticUrgentSafetyOutcome.DuplicatePaperStage, snapshot);

        return new(SyntheticUrgentSafetyOutcome.RecordedUnverified, snapshot.Append(input));
    }

    private static bool Synthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}

// This is only a read of three UNVERIFIED paper stages, not real warning
// delivery, a clinical team's receipt or patient emergency status.
internal readonly record struct SyntheticUrgentSafetyRead(
    string CaseRef, string MemberRef, long Revision,
    bool UnverifiedWarningNotePresent,
    bool UnverifiedReferralRequestNotePresent,
    bool UnverifiedRecipientReceiptNotePresent);

internal static class SyntheticUrgentSafetyProjection
{
    internal static SyntheticUrgentSafetyRead? Project(
        SyntheticUrgentSafetySnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Events.IsEmpty)
            return null;

        var root = snapshot.Events[0].Command;
        return new(root.CaseRef!, root.MemberRef!, snapshot.Revision,
            snapshot.Events.Any(x => x.Command.Action ==
                SyntheticUrgentSafetyAction.RecordUnverifiedWarningEvidence),
            snapshot.Events.Any(x => x.Command.Action ==
                SyntheticUrgentSafetyAction.RecordUnverifiedReferralRequestEvidence),
            snapshot.Events.Any(x => x.Command.Action ==
                SyntheticUrgentSafetyAction.RecordUnverifiedClinicalReceiptEvidence));
    }
}
