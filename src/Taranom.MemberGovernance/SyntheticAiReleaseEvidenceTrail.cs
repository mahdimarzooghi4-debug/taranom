using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// An INTERNAL SYNTHETIC paper trail, NOT an AI trainer, a real model registry,
// real committee authentication, release authorization or Production router.
internal enum SyntheticAiReleaseEvidenceAction
{
    RecordUnverifiedCandidate = 1,
    RecordUnverifiedJointCommitteePacket = 2,
    RecordUnverifiedFinalHumanPacket = 3
}

internal enum SyntheticAiReleaseEvidenceOutcome
{
    RecordedUnverified = 1,
    Replayed = 2,
    Invalid = 3,
    StaleRevision = 4,
    ConflictingReplay = 5,
    MissingCandidate = 6,
    MissingCommitteePacket = 7,
    ScopeMismatch = 8,
    ClaimedActorConflict = 9,
    DuplicateStage = 10
}

// All provenance fields are opaque test refs, NOT trusted medical, legal,
// dataset or model attestations. A claimed artifact SHA-256 is unkeyed data.
internal readonly record struct SyntheticAiReleaseEvidenceCommand(
    Guid RequestId,
    long ExpectedRevision,
    SyntheticAiReleaseEvidenceAction Action,
    string CandidateRef,
    string DatasetVersionRef,
    string ArtifactDigestSha256,
    Guid OriginalCandidateRequestId,
    string? ClaimedMedicalCommitteeActorRef,
    string? ClaimedTechnicalCommitteeActorRef,
    string? UnverifiedJointCommitteePacketRef,
    string? ClaimedFinalAuthorityActorRef,
    string? UnverifiedFinalHumanPacketRef);

internal readonly record struct SyntheticAiReleaseEvidenceEntry(
    long Revision, SyntheticAiReleaseEvidenceCommand Command);

internal sealed class SyntheticAiReleaseEvidenceSnapshot
{
    internal static SyntheticAiReleaseEvidenceSnapshot Empty { get; } =
        new(0, ImmutableArray<SyntheticAiReleaseEvidenceEntry>.Empty);

    private SyntheticAiReleaseEvidenceSnapshot(
        long revision, ImmutableArray<SyntheticAiReleaseEvidenceEntry> events)
    {
        Revision = revision;
        Events = events;
    }

    internal long Revision { get; }
    internal ImmutableArray<SyntheticAiReleaseEvidenceEntry> Events { get; }

    internal SyntheticAiReleaseEvidenceSnapshot Append(
        SyntheticAiReleaseEvidenceCommand command) =>
        new(checked(Revision + 1),
            Events.Add(new(Revision + 1, command)));
}

internal readonly record struct SyntheticAiReleaseEvidenceChange(
    SyntheticAiReleaseEvidenceOutcome Outcome,
    SyntheticAiReleaseEvidenceSnapshot Snapshot);

// A real source, licence, consent scope, quorum or responsible officer cannot
// be inferred from 'test:' labels. Even all three accepted entries never
// yield production promotion or any AI inference capability.
internal static class SyntheticAiReleaseEvidenceTrail
{
    internal static SyntheticAiReleaseEvidenceChange Apply(
        SyntheticAiReleaseEvidenceSnapshot? current,
        SyntheticAiReleaseEvidenceCommand command)
    {
        var history = current ?? SyntheticAiReleaseEvidenceSnapshot.Empty;
        if (current is null || command.RequestId == Guid.Empty ||
            command.ExpectedRevision < 0 || command.ExpectedRevision == long.MaxValue ||
            command.Action is not (
                SyntheticAiReleaseEvidenceAction.RecordUnverifiedCandidate or
                SyntheticAiReleaseEvidenceAction.RecordUnverifiedJointCommitteePacket or
                SyntheticAiReleaseEvidenceAction.RecordUnverifiedFinalHumanPacket) ||
            !Synthetic(command.CandidateRef) ||
            !Synthetic(command.DatasetVersionRef) ||
            !Sha256(command.ArtifactDigestSha256))
            return new(SyntheticAiReleaseEvidenceOutcome.Invalid, history);

        if (command.Action == SyntheticAiReleaseEvidenceAction.RecordUnverifiedCandidate)
        {
            if (command.OriginalCandidateRequestId != Guid.Empty ||
                command.ClaimedMedicalCommitteeActorRef is not null ||
                command.ClaimedTechnicalCommitteeActorRef is not null ||
                command.UnverifiedJointCommitteePacketRef is not null ||
                command.ClaimedFinalAuthorityActorRef is not null ||
                command.UnverifiedFinalHumanPacketRef is not null)
                return new(SyntheticAiReleaseEvidenceOutcome.Invalid, history);
        }
        else if (command.OriginalCandidateRequestId == Guid.Empty)
            return new(SyntheticAiReleaseEvidenceOutcome.Invalid, history);
        else if (command.Action ==
                 SyntheticAiReleaseEvidenceAction.RecordUnverifiedJointCommitteePacket)
        {
            if (!Synthetic(command.ClaimedMedicalCommitteeActorRef) ||
                !Synthetic(command.ClaimedTechnicalCommitteeActorRef) ||
                !Synthetic(command.UnverifiedJointCommitteePacketRef) ||
                command.ClaimedFinalAuthorityActorRef is not null ||
                command.UnverifiedFinalHumanPacketRef is not null)
                return new(SyntheticAiReleaseEvidenceOutcome.Invalid, history);
            if (command.ClaimedMedicalCommitteeActorRef ==
                command.ClaimedTechnicalCommitteeActorRef)
                return new(SyntheticAiReleaseEvidenceOutcome.ClaimedActorConflict, history);
        }
        else
        {
            if (command.ClaimedMedicalCommitteeActorRef is not null ||
                command.ClaimedTechnicalCommitteeActorRef is not null ||
                command.UnverifiedJointCommitteePacketRef is not null ||
                !Synthetic(command.ClaimedFinalAuthorityActorRef) ||
                !Synthetic(command.UnverifiedFinalHumanPacketRef))
                return new(SyntheticAiReleaseEvidenceOutcome.Invalid, history);
        }

        // Check exact historical requests BEFORE revision. Changed request
        // UUID payload cannot silently alter already accepted paper evidence.
        foreach (var previous in history.Events)
        {
            if (previous.Command.RequestId != command.RequestId) continue;
            return new(previous.Command == command
                ? SyntheticAiReleaseEvidenceOutcome.Replayed
                : SyntheticAiReleaseEvidenceOutcome.ConflictingReplay, history);
        }

        if (command.ExpectedRevision != history.Revision)
            return new(SyntheticAiReleaseEvidenceOutcome.StaleRevision, history);

        if (history.Events.IsEmpty)
            return command.Action ==
                   SyntheticAiReleaseEvidenceAction.RecordUnverifiedCandidate
                ? new(SyntheticAiReleaseEvidenceOutcome.RecordedUnverified,
                    history.Append(command))
                : new(SyntheticAiReleaseEvidenceOutcome.MissingCandidate, history);

        // Once a candidate exists, any different new candidate request is
        // always a forbidden second candidate stage; no downstream packet
        // can turn it into a release or replace immutable provenance.
        if (command.Action ==
            SyntheticAiReleaseEvidenceAction.RecordUnverifiedCandidate)
            return new(SyntheticAiReleaseEvidenceOutcome.DuplicateStage, history);

        var original = history.Events[0].Command;
        if (command.CandidateRef != original.CandidateRef ||
            command.DatasetVersionRef != original.DatasetVersionRef ||
            command.ArtifactDigestSha256 != original.ArtifactDigestSha256 ||
            command.OriginalCandidateRequestId != original.RequestId)
            return new(SyntheticAiReleaseEvidenceOutcome.ScopeMismatch, history);

        if (command.Action ==
            SyntheticAiReleaseEvidenceAction.RecordUnverifiedJointCommitteePacket)
            return history.Events.Length == 1
                ? new(SyntheticAiReleaseEvidenceOutcome.RecordedUnverified,
                    history.Append(command))
                : new(SyntheticAiReleaseEvidenceOutcome.DuplicateStage, history);

        if (history.Events.Length < 2 ||
            history.Events[1].Command.Action !=
            SyntheticAiReleaseEvidenceAction.RecordUnverifiedJointCommitteePacket)
            return new(SyntheticAiReleaseEvidenceOutcome.MissingCommitteePacket, history);

        if (history.Events.Length > 2)
            return new(SyntheticAiReleaseEvidenceOutcome.DuplicateStage, history);

        var committee = history.Events[1].Command;
        if (command.ClaimedFinalAuthorityActorRef ==
            committee.ClaimedMedicalCommitteeActorRef ||
            command.ClaimedFinalAuthorityActorRef ==
            committee.ClaimedTechnicalCommitteeActorRef)
            return new(SyntheticAiReleaseEvidenceOutcome.ClaimedActorConflict, history);

        return new(SyntheticAiReleaseEvidenceOutcome.RecordedUnverified,
            history.Append(command));
    }

    private static bool Synthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');

    private static bool Sha256(string? value) =>
        value is { Length: 64 } &&
        value.All(ch => ch is >= '0' and <= '9' or >= 'A' and <= 'F');
}

// Unverified collection diagnostics only; NOT model readiness, committee
// admission, training completion or release authorization.
internal readonly record struct SyntheticAiReleaseEvidenceRead(
    string CandidateRef,
    long Revision,
    bool UnverifiedCandidateRecorded,
    bool UnverifiedCommitteePacketRecorded,
    bool UnverifiedFinalHumanPacketRecorded);

internal static class SyntheticAiReleaseEvidenceProjection
{
    internal static SyntheticAiReleaseEvidenceRead? Project(
        SyntheticAiReleaseEvidenceSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Events.IsEmpty)
            return null;
        return new(
            snapshot.Events[0].Command.CandidateRef,
            snapshot.Revision,
            true,
            snapshot.Events.Any(x => x.Command.Action ==
                SyntheticAiReleaseEvidenceAction.RecordUnverifiedJointCommitteePacket),
            snapshot.Events.Any(x => x.Command.Action ==
                SyntheticAiReleaseEvidenceAction.RecordUnverifiedFinalHumanPacket));
    }
}
