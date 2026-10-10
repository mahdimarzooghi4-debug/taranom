using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// Internal pure reducer, not a database schema, IdP API, member-activation command,
// source-verification mechanism, or source of an authorization decision.
// It stores no applicant name, national identifier, tenant, medical or finance data.
public enum ProofingCaseAction
{
    ObserveProofing = 1,
    ApplicantCorrectedIdentity = 2
}

public enum ProofingJournalOutcome
{
    Applied = 1,
    Replayed = 2,
    InvalidCommand = 3,
    ConflictingReplay = 4,
    StaleCaseRevision = 5,
    StaleIdentityRevision = 6,
    NotApplicable = 7
}

// Correction-specific activation flags are independent of the earlier proofing
// observation. The eventually authorized Membership owner must provide them.
public readonly record struct ProofingCaseCommand(
    Guid RequestId,
    long ExpectedCaseRevision,
    long ExpectedIdentityRevision,
    ProofingCaseAction Action,
    ProofingReviewInput ProofingEvidence,
    EvidenceCheck CurrentOfficialActivationStatusKnown,
    bool IsCurrentlyOfficiallyActivated);

public readonly record struct ProofingCaseEntry(
    long CaseRevision,
    long IdentityRevision,
    ProofingCaseCommand Command,
    ProofingReviewState ResultingReview);

public sealed class ProofingCaseSnapshot
{
    public static ProofingCaseSnapshot Empty { get; } = new(
        0, 0, ProofingReviewState.AwaitingAuthoritativeVerification,
        ImmutableArray<ProofingCaseEntry>.Empty);

    private ProofingCaseSnapshot(long caseRevision, long identityRevision,
        ProofingReviewState review, ImmutableArray<ProofingCaseEntry> history)
    {
        CaseRevision = caseRevision;
        IdentityRevision = identityRevision;
        Review = review;
        History = history;
    }

    public long CaseRevision { get; }
    public long IdentityRevision { get; }
    public ProofingReviewState Review { get; }
    public ImmutableArray<ProofingCaseEntry> History { get; }

    internal ProofingCaseSnapshot Append(ProofingCaseCommand command,
        ProofingReviewState nextReview, long nextIdentityRevision)
    {
        var nextCaseRevision = checked(CaseRevision + 1);
        var item = new ProofingCaseEntry(nextCaseRevision, nextIdentityRevision,
            command, nextReview);
        return new ProofingCaseSnapshot(nextCaseRevision, nextIdentityRevision,
            nextReview, History.Add(item));
    }
}

public readonly record struct ProofingCaseChange(
    ProofingJournalOutcome Outcome,
    ProofingCaseSnapshot Snapshot);

public static class ProofingCaseJournal
{
    public static ProofingCaseChange Apply(
        ProofingCaseSnapshot? current, ProofingCaseCommand command)
    {
        if (current is null || command.RequestId == Guid.Empty ||
            command.ExpectedCaseRevision < 0 || command.ExpectedIdentityRevision < 0 ||
            command.Action is not (ProofingCaseAction.ObserveProofing or ProofingCaseAction.ApplicantCorrectedIdentity))
            return new(ProofingJournalOutcome.InvalidCommand,
                current ?? ProofingCaseSnapshot.Empty);

        // Check the complete history before the version guard: an exact replay
        // after newer events must not reapply/rewind them. Reusing the same key
        // with changed parameters is rejected even if the case revision changed.
        foreach (var entry in current.History)
        {
            if (entry.Command.RequestId != command.RequestId)
                continue;
            return entry.Command == command
                ? new(ProofingJournalOutcome.Replayed, current)
                : new(ProofingJournalOutcome.ConflictingReplay, current);
        }

        if (command.ExpectedCaseRevision != current.CaseRevision)
            return new(ProofingJournalOutcome.StaleCaseRevision, current);
        if (command.ExpectedIdentityRevision != current.IdentityRevision)
            return new(ProofingJournalOutcome.StaleIdentityRevision, current);
        if (current.CaseRevision == long.MaxValue)
            return new(ProofingJournalOutcome.InvalidCommand, current);

        switch (command.Action)
        {
            case ProofingCaseAction.ObserveProofing:
                if (command.CurrentOfficialActivationStatusKnown != EvidenceCheck.Missing ||
                    command.IsCurrentlyOfficiallyActivated)
                    return new(ProofingJournalOutcome.InvalidCommand, current);

                var review = IdentityProofingReview.Assess(command.ProofingEvidence);
                return new(ProofingJournalOutcome.Applied,
                    current.Append(command, review, current.IdentityRevision));

            case ProofingCaseAction.ApplicantCorrectedIdentity:
                // The previous mismatch only establishes a *candidate* for
                // correction. An independent, current activation-status check
                // must be supplied to the future authorized command handler.
                if (command.ProofingEvidence != default)
                    return new(ProofingJournalOutcome.InvalidCommand, current);
                if (current.Review != ProofingReviewState.CorrectApplicantInformationThenReverify ||
                    command.CurrentOfficialActivationStatusKnown != EvidenceCheck.Confirmed ||
                    command.IsCurrentlyOfficiallyActivated)
                    return new(ProofingJournalOutcome.NotApplicable, current);
                if (current.IdentityRevision == long.MaxValue)
                    return new(ProofingJournalOutcome.InvalidCommand, current);

                // Old proofing outcomes are invalid after any identity change.
                // A subsequent observation must explicitly pin the NEW identity
                // revision. This does not initiate/authorize a real verification.
                return new(ProofingJournalOutcome.Applied,
                    current.Append(command,
                        ProofingReviewState.AwaitingAuthoritativeVerification,
                        current.IdentityRevision + 1));

            default:
                return new(ProofingJournalOutcome.InvalidCommand, current);
        }
    }
}
