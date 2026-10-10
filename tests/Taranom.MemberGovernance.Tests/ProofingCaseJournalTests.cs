using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class ProofingCaseJournalTests
{
    private static readonly Guid InitialId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CorrectionId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid NewEvidenceId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private static ProofingReviewInput ValidProof(ProofingObservation observation) => new(
        observation, EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed, false, false);

    private static ProofingCaseCommand Observe(Guid id, long rev, long identityRev,
        ProofingObservation result) => new(id, rev, identityRev,
        ProofingCaseAction.ObserveProofing, ValidProof(result),
        EvidenceCheck.Missing, false);

    private static ProofingCaseCommand Correct(Guid id, long rev, long identityRev) =>
        new(id, rev, identityRev, ProofingCaseAction.ApplicantCorrectedIdentity,
            default, EvidenceCheck.Confirmed, false);

    [Fact]
    public void UnconfiguredCaseAndEmptyRequestKeyFailClosed()
    {
        var invalid = ProofingCaseJournal.Apply(null, default);
        Assert.Equal(ProofingJournalOutcome.InvalidCommand, invalid.Outcome);
        Assert.Same(ProofingCaseSnapshot.Empty, invalid.Snapshot);
        var emptyKey = Observe(Guid.Empty, 0, 0, ProofingObservation.VerifiedMatch);
        Assert.Equal(ProofingJournalOutcome.InvalidCommand,
            ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty, emptyKey).Outcome);
    }

    [Fact]
    public void ProviderOutageIsRecordedAsPendingNotVerified()
    {
        var next = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(InitialId, 0, 0, ProofingObservation.ProviderUnavailable));
        Assert.Equal(ProofingJournalOutcome.Applied, next.Outcome);
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification, next.Snapshot.Review);
        Assert.Equal(1L, next.Snapshot.CaseRevision);
        Assert.Equal(0L, next.Snapshot.IdentityRevision);
    }

    [Fact]
    public void VerifiedProofIsOnlyEvidenceNeverAnActivation()
    {
        var next = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(InitialId, 0, 0, ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.Applied, next.Outcome);
        Assert.Equal(ProofingReviewState.ValidatedProofingEvidenceOnly, next.Snapshot.Review);
        Assert.Equal(0L, next.Snapshot.IdentityRevision);
    }

    [Fact]
    public void VerifiedMismatchThenCorrectionInvalidatesPriorEvidence()
    {
        var mismatchCmd = Observe(InitialId, 0, 0, ProofingObservation.DefinitiveMismatch);
        var mismatch = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty, mismatchCmd);
        Assert.Equal(ProofingReviewState.CorrectApplicantInformationThenReverify, mismatch.Snapshot.Review);
        var corrected = ProofingCaseJournal.Apply(mismatch.Snapshot, Correct(CorrectionId, 1, 0));
        Assert.Equal(ProofingJournalOutcome.Applied, corrected.Outcome);
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification, corrected.Snapshot.Review);
        Assert.Equal(1L, corrected.Snapshot.IdentityRevision);
        Assert.Equal(2L, corrected.Snapshot.CaseRevision);
        Assert.Equal(2, corrected.Snapshot.History.Length);
        Assert.Equal(ProofingReviewState.CorrectApplicantInformationThenReverify,
            corrected.Snapshot.History[0].ResultingReview);
    }

    [Fact]
    public void OldIdentityEvidenceCannotReappearAfterCorrection()
    {
        var mismatch = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(InitialId, 0, 0, ProofingObservation.DefinitiveMismatch));
        var corrected = ProofingCaseJournal.Apply(mismatch.Snapshot,
            Correct(CorrectionId, 1, 0));
        var stale = ProofingCaseJournal.Apply(corrected.Snapshot,
            Observe(NewEvidenceId, 2, 0, ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.StaleIdentityRevision, stale.Outcome);
        Assert.Same(corrected.Snapshot, stale.Snapshot);
    }

    [Fact]
    public void FreshEvidenceAfterCorrectionMayBeReviewedButGrantsNothing()
    {
        var mismatch = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(InitialId, 0, 0, ProofingObservation.DefinitiveMismatch));
        var corrected = ProofingCaseJournal.Apply(mismatch.Snapshot,
            Correct(CorrectionId, 1, 0));
        var reverified = ProofingCaseJournal.Apply(corrected.Snapshot,
            Observe(NewEvidenceId, 2, 1, ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.Applied, reverified.Outcome);
        Assert.Equal(ProofingReviewState.ValidatedProofingEvidenceOnly, reverified.Snapshot.Review);
        Assert.Equal(3L, reverified.Snapshot.CaseRevision);
        Assert.Equal(1L, reverified.Snapshot.IdentityRevision);
    }

    [Fact]
    public void ExactReplayAfterLaterEventsCannotRollbackCase()
    {
        var first = Observe(InitialId, 0, 0, ProofingObservation.DefinitiveMismatch);
        var observed = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty, first);
        var corrected = ProofingCaseJournal.Apply(observed.Snapshot, Correct(CorrectionId, 1, 0));
        var replay = ProofingCaseJournal.Apply(corrected.Snapshot, first);
        Assert.Equal(ProofingJournalOutcome.Replayed, replay.Outcome);
        Assert.Same(corrected.Snapshot, replay.Snapshot);
        Assert.Equal(1L, replay.Snapshot.IdentityRevision);
    }

    [Fact]
    public void ReusedRequestKeyWithDifferentPayloadIsConflict()
    {
        var first = Observe(InitialId, 0, 0, ProofingObservation.DefinitiveMismatch);
        var applied = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty, first);
        var changed = first with { ProofingEvidence = ValidProof(ProofingObservation.VerifiedMatch) };
        var conflict = ProofingCaseJournal.Apply(applied.Snapshot, changed);
        Assert.Equal(ProofingJournalOutcome.ConflictingReplay, conflict.Outcome);
        Assert.Same(applied.Snapshot, conflict.Snapshot);
    }

    [Fact]
    public void ConcurrentDistinctRequestFromStaleCaseCannotAdvance()
    {
        var first = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(InitialId, 0, 0, ProofingObservation.ProviderUnavailable));
        var stale = ProofingCaseJournal.Apply(first.Snapshot,
            Observe(NewEvidenceId, 0, 0, ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.StaleCaseRevision, stale.Outcome);
        Assert.Same(first.Snapshot, stale.Snapshot);
    }

    [Fact]
    public void CorrectionWithoutValidatedMismatchIsNotApplicable()
    {
        var attempt = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty, Correct(CorrectionId, 0, 0));
        Assert.Equal(ProofingJournalOutcome.NotApplicable, attempt.Outcome);
        Assert.Equal(0L, attempt.Snapshot.CaseRevision);
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing, false)]
    [InlineData(EvidenceCheck.Rejected, false)]
    [InlineData(EvidenceCheck.Confirmed, true)]
    public void CorrectionRequiresCurrentIndependentProofOfNotActive(
        EvidenceCheck known, bool active)
    {
        var mismatch = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(InitialId, 0, 0, ProofingObservation.DefinitiveMismatch));
        var correction = Correct(CorrectionId, 1, 0) with
        {
            CurrentOfficialActivationStatusKnown = known,
            IsCurrentlyOfficiallyActivated = active
        };
        Assert.Equal(ProofingJournalOutcome.NotApplicable,
            ProofingCaseJournal.Apply(mismatch.Snapshot, correction).Outcome);
    }

    [Fact]
    public void CrossActionRequestReuseAlwaysConflicts()
    {
        var mismatch = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(InitialId, 0, 0, ProofingObservation.DefinitiveMismatch));
        var reused = Correct(InitialId, 1, 0);
        Assert.Equal(ProofingJournalOutcome.ConflictingReplay,
            ProofingCaseJournal.Apply(mismatch.Snapshot, reused).Outcome);
    }

    [Fact]
    public void UnknownActionAndNegativeVersionAreRejected()
    {
        var invalid = Observe(InitialId, 0, 0, ProofingObservation.VerifiedMatch)
            with { Action = (ProofingCaseAction)77 };
        Assert.Equal(ProofingJournalOutcome.InvalidCommand,
            ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty, invalid).Outcome);
        Assert.Equal(ProofingJournalOutcome.InvalidCommand,
            ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
                Observe(InitialId, -1, 0, ProofingObservation.VerifiedMatch)).Outcome);
    }

    [Fact]
    public void ObservationRejectsAccidentalCorrectionStatusFields()
    {
        var cmd = Observe(InitialId, 0, 0, ProofingObservation.VerifiedMatch)
            with { CurrentOfficialActivationStatusKnown = EvidenceCheck.Confirmed };
        Assert.Equal(ProofingJournalOutcome.InvalidCommand,
            ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty, cmd).Outcome);
    }

    [Fact]
    public void CorrectionCannotEmbedStaleProviderEvidence()
    {
        var mismatch = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(InitialId, 0, 0, ProofingObservation.DefinitiveMismatch));
        var cmd = Correct(CorrectionId, 1, 0)
            with { ProofingEvidence = ValidProof(ProofingObservation.VerifiedMatch) };
        Assert.Equal(ProofingJournalOutcome.InvalidCommand,
            ProofingCaseJournal.Apply(mismatch.Snapshot, cmd).Outcome);
    }
}
