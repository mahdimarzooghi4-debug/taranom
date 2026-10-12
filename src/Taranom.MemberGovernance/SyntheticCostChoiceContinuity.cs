using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// TRN-BIZ-051/052: PURE, INTERNAL, TEST-ONLY continuity inspection. Reuses
// existing five-source preview and version-pinned CostAwarenessRecorder.
// A paper "choice" never confers informed clinical consent, financial debt,
// provider acceptance, payment, treatment permission or wallet reservation.
internal enum SyntheticCostChoiceContinuityState
{
    InvalidSource = 1,
    PreviousChoiceHistoryInvalid = 2,
    MemberScopeMismatch = 3,
    NoPriorChoiceToCarry = 4,
    UnverifiedSamePreviewOnly = 5,
    ChangedPreviewRequiresSeparateReview = 6,
    CurrentMissingRequiresSeparateReview = 7,
    CurrentConflictRequiresSeparateReview = 8
}

internal readonly record struct SyntheticCostChoiceContinuityRead(
    SyntheticCostChoiceContinuityState State,
    long PreviousUnverifiedChoiceRevision,
    long PreviousPreviewRevision,
    long CurrentPreviewRevision,
    CostAwarenessChoice? PreviousUnverifiedChoice,
    ImmutableArray<SyntheticCostComponent> ChangedComponents,
    bool ChoiceCarriedForward,
    bool FinancialCommitmentCreated,
    bool ClinicalConsentCreated);

// This is a read from caller-supplied synthetic fixtures. No actual member
// authentication or verification of the underlying source documents.
internal static class SyntheticCostChoiceContinuity
{
    internal static SyntheticCostChoiceContinuityRead Inspect(
        SyntheticCostPreview previous,
        CostAwarenessSnapshot? claimedChoiceHistory,
        SyntheticCostPreview current)
    {
        static SyntheticCostChoiceContinuityRead Fail(
            SyntheticCostChoiceContinuityState state) =>
            new(state, 0, 0, 0, null,
                ImmutableArray<SyntheticCostComponent>.Empty, false, false, false);

        if (!Canonical(previous) || !Canonical(current))
            return Fail(SyntheticCostChoiceContinuityState.InvalidSource);
        if (previous.MemberRef != current.MemberRef)
            return Fail(SyntheticCostChoiceContinuityState.MemberScopeMismatch);

        if (claimedChoiceHistory is null ||
            claimedChoiceHistory.MemberRef != previous.MemberRef ||
            claimedChoiceHistory.PreviewId != previous.PreviewId ||
            claimedChoiceHistory.PreviewRevision != previous.PreviewRevision ||
            claimedChoiceHistory.PreviewFingerprint != previous.Fingerprint ||
            claimedChoiceHistory.Revision < 0 ||
            claimedChoiceHistory.Revision != claimedChoiceHistory.History.Length)
            return Fail(SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid);

        // Rebuild the exact historical choices with the existing domain
        // reducer, not an invented replacement or a "last flag wins" shortcut.
        var replay = CostAwarenessSnapshot.FromPreview(previous);
        if (replay is null)
            return Fail(SyntheticCostChoiceContinuityState.InvalidSource);
        foreach (var entry in claimedChoiceHistory.History)
        {
            var result = CostAwarenessRecorder.Record(replay, entry.Command);
            if (result.Outcome != CostAwarenessOutcome.RecordedUnverified ||
                result.Snapshot is null ||
                result.Snapshot.Revision != entry.Revision)
                return Fail(SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid);
            replay = result.Snapshot;
        }
        if (replay.Revision != claimedChoiceHistory.Revision)
            return Fail(SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid);

        CostAwarenessChoice? choice = replay.History.IsEmpty
            ? null : replay.History[^1].Command.Choice;
        var changed = ImmutableArray.CreateBuilder<SyntheticCostComponent>();
        foreach (var oldLine in previous.Lines)
        {
            var newLine = current.Lines.Single(x => x.Component == oldLine.Component);
            if (newLine != oldLine)
                changed.Add(oldLine.Component);
        }

        // Even if the five underlying source values are identical, a newly
        // issued preview ID or revision must be separately re-presented.
        var same = previous.PreviewId == current.PreviewId &&
                   previous.PreviewRevision == current.PreviewRevision &&
                   previous.Fingerprint == current.Fingerprint &&
                   changed.Count == 0;
        var state = choice is null
            ? SyntheticCostChoiceContinuityState.NoPriorChoiceToCarry
            : same
                ? SyntheticCostChoiceContinuityState.UnverifiedSamePreviewOnly
                : current.State == SyntheticCostPreviewState.Conflicted
                    ? SyntheticCostChoiceContinuityState.CurrentConflictRequiresSeparateReview
                    : current.State == SyntheticCostPreviewState.MissingOrIncomplete
                        ? SyntheticCostChoiceContinuityState.CurrentMissingRequiresSeparateReview
                        : SyntheticCostChoiceContinuityState.ChangedPreviewRequiresSeparateReview;
        return new(state, replay.Revision, previous.PreviewRevision,
            current.PreviewRevision, choice, changed.ToImmutable(),
            false, false, false);
    }

    private static bool Canonical(SyntheticCostPreview input)
    {
        if (input.State == SyntheticCostPreviewState.Invalid ||
            input.Lines.IsDefault ||
            input.Fingerprint is not { Length: 64 })
            return false;
        var rebuilt = SyntheticCostPreviewBuilder.Build(input.MemberRef,
            input.PreviewId, input.PreviewRevision, input.Lines);
        return rebuilt.State == input.State &&
               rebuilt.Fingerprint == input.Fingerprint &&
               rebuilt.MemberRef == input.MemberRef &&
               rebuilt.PreviewRevision == input.PreviewRevision &&
               rebuilt.PreviewId == input.PreviewId &&
               rebuilt.Lines.SequenceEqual(input.Lines);
    }
}
