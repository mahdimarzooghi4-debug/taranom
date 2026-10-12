namespace Taranom.MemberGovernance;

// TRN-TECH-094: pure synthetic replay and continuity boundary. The caller
// supplies untrusted paper history on every request; NOTHING is persisted.
// Server revalidates each entry through the original internal recorder.
public sealed record SyntheticChoicePreviewInput(
    string? MemberRef, Guid PreviewId, long PreviewRevision,
    IReadOnlyList<SyntheticCostWorkbenchLine> Lines);

public sealed record SyntheticChoiceCommandInput(
    Guid RequestId, long ExpectedRevision, string? MemberRef,
    Guid PreviewId, long PreviewRevision, string? PreviewFingerprint,
    string? Choice);

public sealed record SyntheticChoiceWorkbenchRead(
    string Outcome, string Continuity, long HistoryRevision,
    string? LastUnverifiedChoice, IReadOnlyList<string> ChangedComponents,
    bool ChoiceCarriedForward, bool FinancialCommitmentCreated,
    bool ClinicalConsentGranted, bool WalletMutated,
    bool MemberAuthenticated, bool NationalReleaseAllowed);

public static class SyntheticCostChoiceWorkbench
{
    private static SyntheticChoiceWorkbenchRead Deny() =>
        new("Invalid", "InvalidSource", 0, null, Array.Empty<string>(),
            false, false, false, false, false, false);

    public static SyntheticChoiceWorkbenchRead Inspect(
        SyntheticChoicePreviewInput? previous,
        SyntheticChoicePreviewInput? current,
        IReadOnlyList<SyntheticChoiceCommandInput>? history,
        SyntheticChoiceCommandInput? attempt)
    {
        if (previous is null || current is null || history is null ||
            history.Count > 16)
            return Deny();
        var old = SyntheticCostWorkbench.BuildCanonical(
            previous.MemberRef, previous.PreviewId, previous.PreviewRevision, previous.Lines);
        var now = SyntheticCostWorkbench.BuildCanonical(
            current.MemberRef, current.PreviewId, current.PreviewRevision, current.Lines);
        if (old.State == SyntheticCostPreviewState.Invalid ||
            now.State == SyntheticCostPreviewState.Invalid)
            return Deny();
        var snapshot = CostAwarenessSnapshot.FromPreview(old);
        if (snapshot is null) return Deny();

        static CostAwarenessCommand? Convert(SyntheticChoiceCommandInput input)
        {
            if (!Enum.TryParse<CostAwarenessChoice>(input.Choice, false, out var choice) ||
                !Enum.IsDefined(choice) || input.Choice != choice.ToString() ||
                input.MemberRef is null || input.PreviewFingerprint is null)
                return null;
            return new(input.RequestId, input.ExpectedRevision,
                input.MemberRef, input.PreviewId, input.PreviewRevision,
                input.PreviewFingerprint, choice);
        }

        // Never trust a supplied revision/count/choice flag; replay *each*
        // exact command from canonical preview at revision zero.
        foreach (var entry in history)
        {
            var parsed = Convert(entry);
            if (parsed is null) return Deny();
            var change = CostAwarenessRecorder.Record(snapshot, parsed.Value);
            if (change.Outcome != CostAwarenessOutcome.RecordedUnverified ||
                change.Snapshot is null) return Deny();
            snapshot = change.Snapshot;
        }

        var outcome = "InspectionOnly";
        if (attempt is not null)
        {
            // A command tied to a previously presented version cannot silently
            // be interpreted as a fresh choice against a changed preview.
            if (old.PreviewId != now.PreviewId ||
                old.PreviewRevision != now.PreviewRevision ||
                old.Fingerprint != now.Fingerprint)
            {
                var changed = SyntheticCostChoiceContinuity.Inspect(old, snapshot, now);
                if (changed.State is SyntheticCostChoiceContinuityState.InvalidSource or
                    SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid or
                    SyntheticCostChoiceContinuityState.MemberScopeMismatch)
                    return Deny();
                return new("PreviewChanged", changed.State.ToString(), snapshot.Revision,
                    changed.PreviousUnverifiedChoice?.ToString(),
                    changed.ChangedComponents.Select(x => x.ToString()).ToArray(),
                    false, false, false, false, false, false);
            }
            var command = Convert(attempt);
            if (command is null) return Deny();
            var change = CostAwarenessRecorder.Record(snapshot, command.Value);
            outcome = change.Outcome.ToString();
            if (change.Outcome == CostAwarenessOutcome.Invalid) return Deny();
            if (change.Outcome == CostAwarenessOutcome.RecordedUnverified)
            {
                if (change.Snapshot is null) return Deny();
                snapshot = change.Snapshot;
            }
        }

        var continuity = SyntheticCostChoiceContinuity.Inspect(old, snapshot, now);
        if (continuity.State is SyntheticCostChoiceContinuityState.InvalidSource or
            SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid or
            SyntheticCostChoiceContinuityState.MemberScopeMismatch)
            return Deny();

        return new(outcome, continuity.State.ToString(), snapshot.Revision,
            continuity.PreviousUnverifiedChoice?.ToString(),
            continuity.ChangedComponents.Select(x => x.ToString()).ToArray(),
            false, false, false, false, false, false);
    }
}
