using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// Claimed user response is a nonbinding synthetic observation, never an
// authenticated consent, treatment authorization or financial commitment.
internal enum CostAwarenessChoice
{
    AcknowledgedUncertainty = 1,
    Deferred = 2,
    RequestedClarification = 3
}

internal enum CostAwarenessOutcome
{
    RecordedUnverified = 1,
    Replayed = 2,
    Invalid = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    PreviewChanged = 6,
    ScopeMismatch = 7
}

internal readonly record struct CostAwarenessCommand(
    Guid RequestId, long ExpectedRevision,
    string MemberRef, Guid PreviewId, long PreviewRevision,
    string PreviewFingerprint, CostAwarenessChoice Choice);

internal readonly record struct CostAwarenessEntry(
    long Revision, CostAwarenessCommand Command);

internal sealed class CostAwarenessSnapshot
{
    internal string MemberRef { get; }
    internal Guid PreviewId { get; }
    internal long PreviewRevision { get; }
    internal string PreviewFingerprint { get; }
    internal long Revision { get; }
    internal ImmutableArray<CostAwarenessEntry> History { get; }

    private CostAwarenessSnapshot(string memberRef, Guid previewId,
        long previewRevision, string fingerprint, long revision,
        ImmutableArray<CostAwarenessEntry> history)
    {
        MemberRef = memberRef;
        PreviewId = previewId;
        PreviewRevision = previewRevision;
        PreviewFingerprint = fingerprint;
        Revision = revision;
        History = history;
    }

    internal static CostAwarenessSnapshot? FromPreview(SyntheticCostPreview report) =>
        report.State == SyntheticCostPreviewState.Invalid ||
        !SyntheticCostPreviewBuilder.Synthetic(report.MemberRef) ||
        report.PreviewId == Guid.Empty || report.PreviewRevision <= 0 ||
        report.Fingerprint is not { Length: 64 } || report.Lines.Length != 5
            ? null
            : new(report.MemberRef!, report.PreviewId, report.PreviewRevision,
                report.Fingerprint, 0, ImmutableArray<CostAwarenessEntry>.Empty);

    internal CostAwarenessSnapshot Append(CostAwarenessCommand command) =>
        new(MemberRef, PreviewId, PreviewRevision, PreviewFingerprint,
            checked(Revision + 1), History.Add(new(Revision + 1, command)));
}

internal readonly record struct CostAwarenessChange(
    CostAwarenessOutcome Outcome, CostAwarenessSnapshot? Snapshot);

internal static class CostAwarenessRecorder
{
    internal static CostAwarenessChange Record(
        CostAwarenessSnapshot? current, CostAwarenessCommand input)
    {
        if (current is null || input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 || input.ExpectedRevision == long.MaxValue ||
            !SyntheticCostPreviewBuilder.Synthetic(input.MemberRef) ||
            input.PreviewId == Guid.Empty || input.PreviewRevision <= 0 ||
            input.PreviewFingerprint is not { Length: 64 } ||
            !Enum.IsDefined(input.Choice))
            return new(CostAwarenessOutcome.Invalid, current);

        // Exact replays remain idempotent, even after later choices.
        foreach (var prior in current.History)
        {
            if (prior.Command.RequestId != input.RequestId) continue;
            return new(prior.Command == input
                ? CostAwarenessOutcome.Replayed
                : CostAwarenessOutcome.ConflictingReplay, current);
        }

        if (input.MemberRef != current.MemberRef ||
            input.PreviewId != current.PreviewId)
            return new(CostAwarenessOutcome.ScopeMismatch, current);
        if (input.PreviewRevision != current.PreviewRevision ||
            input.PreviewFingerprint != current.PreviewFingerprint)
            return new(CostAwarenessOutcome.PreviewChanged, current);
        if (input.ExpectedRevision != current.Revision)
            return new(CostAwarenessOutcome.StaleRevision, current);
        return new(CostAwarenessOutcome.RecordedUnverified, current.Append(input));
    }
}
