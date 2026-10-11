using System.Collections.Immutable;

namespace Taranom.MemberGovernance;

// TRN-BIZ-048/049/050: INTERNAL pure synthetic-only insurer dispute paper
// lineage. NO insurer connection, coverage decision, claim adjustment,
// official invoice, wallet, settlement, refund or medical record.
internal enum SyntheticInsuranceRoute
{
    DirectLicensedInsurer = 1,
    LicensedIntermediary = 2
}

internal enum SyntheticInsuranceHandoffAction
{
    OpenUnverifiedInsurerDisputeScope = 1,
    RecordUnverifiedForwardRequest = 2,
    RecordUnverifiedInsurerReceipt = 3,
    RecordUnverifiedInsurerResponseSource = 4
}

internal enum SyntheticInsuranceHandoffOutcome
{
    Invalid = 1,
    RecordedUnverified = 2,
    Replayed = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    SourceInvalid = 6,
    SourceChanged = 7,
    ScopeMismatch = 8,
    OriginalMissing = 9,
    PreviousStageMissing = 10,
    ClaimedRecipientMismatch = 11,
    DuplicateStage = 12
}

internal readonly record struct SyntheticInsuranceHandoffCommand(
    Guid RequestId,
    long ExpectedRevision,
    SyntheticInsuranceHandoffAction Action,
    string? CaseRef,
    string? MemberRef,
    string? TreatmentRef,
    Guid BillPacketId,
    SyntheticBillLane DisputedInsurerLane,
    Guid DisputedEvidenceId,
    long DisputedLaneRevision,
    SyntheticInsuranceRoute Route,
    string? ClaimedInsurerRef,
    string? ClaimedIntermediaryRef,
    Guid OriginalScopeRequestId,
    Guid OriginalForwardRequestId,
    string? ClaimedActorRef,
    string? UnverifiedEvidenceRef);

internal readonly record struct SyntheticInsuranceHandoffEntry(
    long Revision, SyntheticInsuranceHandoffCommand Command);

internal sealed class SyntheticInsuranceHandoffSnapshot
{
    internal static SyntheticInsuranceHandoffSnapshot Empty { get; } =
        new(0, ImmutableArray<SyntheticInsuranceHandoffEntry>.Empty, null, 0);

    private SyntheticInsuranceHandoffSnapshot(
        long revision, ImmutableArray<SyntheticInsuranceHandoffEntry> history,
        string? sourceFingerprint, long supportRevision)
    {
        Revision = revision;
        History = history;
        PinnedBillFingerprint = sourceFingerprint;
        PinnedSupportRevision = supportRevision;
    }

    internal long Revision { get; }
    internal ImmutableArray<SyntheticInsuranceHandoffEntry> History { get; }
    internal string? PinnedBillFingerprint { get; }
    internal long PinnedSupportRevision { get; }

    internal SyntheticInsuranceHandoffSnapshot Append(
        SyntheticInsuranceHandoffCommand command,
        string sourceFingerprint, long supportRevision) =>
        new(checked(Revision + 1),
            History.Add(new(Revision + 1, command)),
            sourceFingerprint, supportRevision);
}

internal readonly record struct SyntheticInsuranceHandoffChange(
    SyntheticInsuranceHandoffOutcome Outcome,
    SyntheticInsuranceHandoffSnapshot Snapshot);

internal static class SyntheticInsuranceDisputeTrail
{
    // Both source contexts are caller-supplied UNVERIFIED histories. Every
    // operation replays the separate support intake, rebuilds the disputed
    // bill and rechecks that the original pinned version has not changed.
    // Never infer a permitted legal route from claimed actor markers.
    internal static SyntheticInsuranceHandoffChange Apply(
        SyntheticInsuranceHandoffSnapshot? current,
        SyntheticInsuranceHandoffCommand input,
        SyntheticSupportSnapshot? support,
        long billViewRevision,
        IReadOnlyList<SyntheticBillEvidence>? billEvidence)
    {
        var snapshot = current ?? SyntheticInsuranceHandoffSnapshot.Empty;
        static SyntheticInsuranceHandoffChange Deny(
            SyntheticInsuranceHandoffOutcome outcome,
            SyntheticInsuranceHandoffSnapshot state) => new(outcome, state);

        if (current is null || input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 || input.ExpectedRevision == long.MaxValue ||
            input.Action is not (
                SyntheticInsuranceHandoffAction.OpenUnverifiedInsurerDisputeScope or
                SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest or
                SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt or
                SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerResponseSource) ||
            !SyntheticCostPreviewBuilder.Synthetic(input.CaseRef) ||
            !SyntheticCostPreviewBuilder.Synthetic(input.MemberRef) ||
            !SyntheticCostPreviewBuilder.Synthetic(input.TreatmentRef) ||
            !SyntheticCostPreviewBuilder.Synthetic(input.ClaimedInsurerRef) ||
            input.BillPacketId == Guid.Empty ||
            input.DisputedEvidenceId == Guid.Empty ||
            input.DisputedLaneRevision < 1 ||
            input.DisputedInsurerLane is not (
                SyntheticBillLane.PrimaryInsurance or
                SyntheticBillLane.SupplementalInsurance) ||
            input.Route is not (
                SyntheticInsuranceRoute.DirectLicensedInsurer or
                SyntheticInsuranceRoute.LicensedIntermediary) ||
            (input.Route == SyntheticInsuranceRoute.DirectLicensedInsurer &&
                input.ClaimedIntermediaryRef is not null) ||
            (input.Route == SyntheticInsuranceRoute.LicensedIntermediary &&
                !SyntheticCostPreviewBuilder.Synthetic(input.ClaimedIntermediaryRef)))
            return Deny(SyntheticInsuranceHandoffOutcome.Invalid, snapshot);

        if (input.Action == SyntheticInsuranceHandoffAction.OpenUnverifiedInsurerDisputeScope)
        {
            if (input.OriginalScopeRequestId != Guid.Empty ||
                input.OriginalForwardRequestId != Guid.Empty ||
                input.ClaimedActorRef is not null ||
                input.UnverifiedEvidenceRef is not null)
                return Deny(SyntheticInsuranceHandoffOutcome.Invalid, snapshot);
        }
        else if (input.OriginalScopeRequestId == Guid.Empty ||
            !SyntheticCostPreviewBuilder.Synthetic(input.ClaimedActorRef) ||
            !SyntheticCostPreviewBuilder.Synthetic(input.UnverifiedEvidenceRef) ||
            (input.Action == SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest &&
                input.OriginalForwardRequestId != Guid.Empty) ||
            (input.Action != SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest &&
                input.OriginalForwardRequestId == Guid.Empty))
            return Deny(SyntheticInsuranceHandoffOutcome.Invalid, snapshot);

        if (support is null || support.History.IsEmpty ||
            billEvidence is null || billViewRevision < 1)
            return Deny(SyntheticInsuranceHandoffOutcome.SourceInvalid, snapshot);

        // Recompute the existing support reducer, including every historical
        // immutable scope/UUID/revision. Never treat a topic marker as official
        // insurer approval or privacy consent.
        var replay = SyntheticSupportSnapshot.Empty;
        foreach (var entry in support.History)
        {
            var applied = SyntheticSupportIntake.Apply(replay, entry.Command);
            if (applied.Outcome != SyntheticSupportOutcome.RecordedUnverified ||
                applied.Snapshot.Revision != entry.Revision)
                return Deny(SyntheticInsuranceHandoffOutcome.SourceInvalid, snapshot);
            replay = applied.Snapshot;
        }
        if (replay.Revision != support.Revision ||
            replay.History[0].Command.Topic != SyntheticSupportTopic.InsuranceCoverageDispute ||
            replay.History[0].Command.CaseRef != input.CaseRef ||
            replay.History[0].Command.MemberRef != input.MemberRef)
            return Deny(SyntheticInsuranceHandoffOutcome.SourceInvalid, snapshot);

        // Compose the existing bill owner; a non-insurer lane or a stale
        // pre-dispute numeric claim is NOT a disputed authoritative source.
        var view = SyntheticTreatmentBillingAssembler.Build(input.MemberRef,
            input.TreatmentRef, input.BillPacketId, billViewRevision, billEvidence);
        if (view.State != SyntheticBillViewState.Disputed ||
            view.EvidenceFingerprint is null)
            return Deny(SyntheticInsuranceHandoffOutcome.SourceInvalid, snapshot);
        var lane = view.Lines.Single(x => x.Lane == input.DisputedInsurerLane);
        if (lane.LastChange != SyntheticBillEvidenceChange.Disputed ||
            lane.LatestEvidenceId != input.DisputedEvidenceId ||
            lane.LaneRevision != input.DisputedLaneRevision)
            return Deny(SyntheticInsuranceHandoffOutcome.SourceInvalid, snapshot);

        if (!snapshot.History.IsEmpty &&
            (snapshot.PinnedBillFingerprint != view.EvidenceFingerprint ||
             snapshot.PinnedSupportRevision != support.Revision))
            return Deny(SyntheticInsuranceHandoffOutcome.SourceChanged, snapshot);

        // Strict complete-input idempotency, independent of newer notes.
        foreach (var prior in snapshot.History)
        {
            if (prior.Command.RequestId != input.RequestId) continue;
            return Deny(prior.Command == input
                ? SyntheticInsuranceHandoffOutcome.Replayed
                : SyntheticInsuranceHandoffOutcome.ConflictingReplay, snapshot);
        }

        if (input.ExpectedRevision != snapshot.Revision)
            return Deny(SyntheticInsuranceHandoffOutcome.StaleRevision, snapshot);

        if (snapshot.History.IsEmpty)
            return input.Action ==
                SyntheticInsuranceHandoffAction.OpenUnverifiedInsurerDisputeScope
                ? new(SyntheticInsuranceHandoffOutcome.RecordedUnverified,
                    snapshot.Append(input, view.EvidenceFingerprint, support.Revision))
                : Deny(SyntheticInsuranceHandoffOutcome.OriginalMissing, snapshot);

        var origin = snapshot.History[0].Command;
        if (input.Action ==
                SyntheticInsuranceHandoffAction.OpenUnverifiedInsurerDisputeScope ||
            input.OriginalScopeRequestId != origin.RequestId ||
            input.CaseRef != origin.CaseRef ||
            input.MemberRef != origin.MemberRef ||
            input.TreatmentRef != origin.TreatmentRef ||
            input.BillPacketId != origin.BillPacketId ||
            input.DisputedInsurerLane != origin.DisputedInsurerLane ||
            input.DisputedEvidenceId != origin.DisputedEvidenceId ||
            input.DisputedLaneRevision != origin.DisputedLaneRevision ||
            input.Route != origin.Route ||
            input.ClaimedInsurerRef != origin.ClaimedInsurerRef ||
            input.ClaimedIntermediaryRef != origin.ClaimedIntermediaryRef)
            return Deny(SyntheticInsuranceHandoffOutcome.ScopeMismatch, snapshot);

        var forwarding = snapshot.History.FirstOrDefault(x =>
            x.Command.Action ==
                SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest);
        var receipt = snapshot.History.FirstOrDefault(x =>
            x.Command.Action ==
                SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt);

        if (input.Action == SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest)
        {
            if (forwarding.Command.RequestId != Guid.Empty)
                return Deny(SyntheticInsuranceHandoffOutcome.DuplicateStage, snapshot);
        }
        else if (forwarding.Command.RequestId == Guid.Empty ||
                 input.OriginalForwardRequestId != forwarding.Command.RequestId)
            return Deny(SyntheticInsuranceHandoffOutcome.PreviousStageMissing, snapshot);
        else if (input.ClaimedActorRef == forwarding.Command.ClaimedActorRef)
            return Deny(SyntheticInsuranceHandoffOutcome.ClaimedRecipientMismatch, snapshot);
        else if (input.Action ==
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt)
        {
            if (receipt.Command.RequestId != Guid.Empty)
                return Deny(SyntheticInsuranceHandoffOutcome.DuplicateStage, snapshot);
        }
        else if (receipt.Command.RequestId == Guid.Empty)
            return Deny(SyntheticInsuranceHandoffOutcome.PreviousStageMissing, snapshot);
        else if (snapshot.History.Any(x => x.Command.Action ==
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerResponseSource))
            return Deny(SyntheticInsuranceHandoffOutcome.DuplicateStage, snapshot);

        // NEVER update bill evidence, insurance entitlement, wallet or
        // official dispute state. Even a receipt/response is only a paper note.
        return new(SyntheticInsuranceHandoffOutcome.RecordedUnverified,
            snapshot.Append(input, view.EvidenceFingerprint, support.Revision));
    }
}

internal readonly record struct SyntheticInsuranceHandoffRead(
    string CaseRef, string MemberRef, SyntheticBillLane DisputedInsurerLane,
    long Revision, bool UnverifiedForwardNotePresent,
    bool UnverifiedReceiptNotePresent, bool UnverifiedResponseSourceNotePresent);

internal static class SyntheticInsuranceDisputeProjection
{
    internal static SyntheticInsuranceHandoffRead? Project(
        SyntheticInsuranceHandoffSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.History.IsEmpty) return null;
        var initial = snapshot.History[0].Command;
        return new(initial.CaseRef!, initial.MemberRef!, initial.DisputedInsurerLane,
            snapshot.Revision,
            snapshot.History.Any(x => x.Command.Action ==
                SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest),
            snapshot.History.Any(x => x.Command.Action ==
                SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt),
            snapshot.History.Any(x => x.Command.Action ==
                SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerResponseSource));
    }
}
