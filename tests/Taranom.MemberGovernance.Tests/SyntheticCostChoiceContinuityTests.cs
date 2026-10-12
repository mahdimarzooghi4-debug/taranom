using System.Collections.Immutable;
using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticCostChoiceContinuityTests
{
    private const string Member = "test:member-a";
    private static readonly Guid PreviewId =
        Guid.Parse("00000000-0000-4000-8000-000000000049");

    private static SyntheticCostLine Line(SyntheticCostComponent component,
        SyntheticCostEvidenceState state = SyntheticCostEvidenceState.ProvisionalClaim,
        decimal amount = 10m) =>
        state switch
        {
            SyntheticCostEvidenceState.Missing =>
                new(component, state, null, null, null, Guid.Empty, 0),
            SyntheticCostEvidenceState.ConflictingClaims =>
                new(component, state, null, null, "test:conflicting-source",
                    Guid.NewGuid(), 1),
            _ => new(component, state, amount, "IRR",
                "test:unverified-provider-or-insurer", Guid.NewGuid(), 1)
        };

    private static SyntheticCostLine[] Lines() =>
        Enum.GetValues<SyntheticCostComponent>().Select(x => Line(x)).ToArray();

    private static SyntheticCostPreview Build(SyntheticCostLine[] lines,
        string member = Member, Guid? id = null, long revision = 1) =>
        SyntheticCostPreviewBuilder.Build(member, id ?? PreviewId, revision, lines);

    private static CostAwarenessSnapshot History(
        SyntheticCostPreview preview,
        params CostAwarenessChoice[] choices)
    {
        var snapshot = CostAwarenessSnapshot.FromPreview(preview)!;
        foreach (var choice in choices)
        {
            var cmd = new CostAwarenessCommand(Guid.NewGuid(), snapshot.Revision,
                preview.MemberRef!, preview.PreviewId, preview.PreviewRevision,
                preview.Fingerprint!, choice);
            var next = CostAwarenessRecorder.Record(snapshot, cmd);
            Assert.Equal(CostAwarenessOutcome.RecordedUnverified, next.Outcome);
            snapshot = next.Snapshot!;
        }
        return snapshot;
    }

    private static SyntheticCostChoiceContinuityRead Inspect(
        SyntheticCostPreview oldPreview, CostAwarenessSnapshot history,
        SyntheticCostPreview current) =>
        SyntheticCostChoiceContinuity.Inspect(oldPreview, history, current);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SameUnchangedPreviewOnlyReportsPreviousUnverifiedChoice(int choiceCode)
    {
        var p = Build(Lines());
        var h = History(p, (CostAwarenessChoice)choiceCode);
        var r = Inspect(p, h, p);
        Assert.Equal(SyntheticCostChoiceContinuityState.UnverifiedSamePreviewOnly, r.State);
        Assert.Equal(1, r.PreviousUnverifiedChoiceRevision);
        Assert.Equal((CostAwarenessChoice)choiceCode, r.PreviousUnverifiedChoice);
        Assert.Empty(r.ChangedComponents);
        Assert.False(r.ChoiceCarriedForward);
        Assert.False(r.FinancialCommitmentCreated);
        Assert.False(r.ClinicalConsentCreated);
    }

    [Fact]
    public void NewPreviewIdWithIdenticalLinesMustNotInheritOldMemberChoice()
    {
        var lines = Lines();
        var old = Build(lines);
        var current = Build(lines, id: Guid.NewGuid());
        var r = Inspect(old, History(old, CostAwarenessChoice.AcknowledgedUncertainty), current);
        Assert.Equal(SyntheticCostChoiceContinuityState.ChangedPreviewRequiresSeparateReview, r.State);
        Assert.Empty(r.ChangedComponents);
        Assert.False(r.ChoiceCarriedForward);
    }

    [Fact]
    public void RevisionChangeWithIdenticalLinesStillRequiresIndependentPresentation()
    {
        var lines = Lines(); var old = Build(lines);
        var now = Build(lines, revision: 2);
        var r = Inspect(old, History(old, CostAwarenessChoice.Deferred), now);
        Assert.Equal(SyntheticCostChoiceContinuityState.ChangedPreviewRequiresSeparateReview, r.State);
        Assert.Equal(1, r.PreviousPreviewRevision);
        Assert.Equal(2, r.CurrentPreviewRevision);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryIndividualFinancialSourceChangeTriggersSeparateReview(int component)
    {
        var oldLines = Lines();
        var p = Build(oldLines);
        var nowLines = oldLines.ToArray();
        var source = nowLines[component - 1];
        nowLines[component - 1] = source with { ClaimedAmount = 25m };
        var r = Inspect(p, History(p, CostAwarenessChoice.AcknowledgedUncertainty),
            Build(nowLines, revision: 2));
        Assert.Equal(SyntheticCostChoiceContinuityState.ChangedPreviewRequiresSeparateReview, r.State);
        Assert.Equal(new[] { (SyntheticCostComponent)component },
            r.ChangedComponents.ToArray());
        Assert.False(r.FinancialCommitmentCreated);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("source-revision")]
    [InlineData("evidence-id")]
    public void SameClaimedAmountNewProvenanceCannotReusePriorChoice(string change)
    {
        var lines = Lines();
        var p = Build(lines);
        var now = lines.ToArray();
        now[1] = change switch
        {
            "source" => now[1] with { UnverifiedSourceRef = "test:other-insurer" },
            "source-revision" => now[1] with { SourceRevision = 2 },
            "evidence-id" => now[1] with { EvidenceRef = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        var r = Inspect(p, History(p, CostAwarenessChoice.AcknowledgedUncertainty),
            Build(now, revision: 2));
        Assert.Equal(SyntheticCostChoiceContinuityState.ChangedPreviewRequiresSeparateReview, r.State);
        Assert.Contains(SyntheticCostComponent.PrimaryInsurance, r.ChangedComponents);
    }

    [Fact]
    public void MissingInsuranceDoesNotBecomeZeroOrOldCoverage()
    {
        var lines = Lines();
        var p = Build(lines);
        var next = lines.ToArray();
        next[1] = Line(SyntheticCostComponent.PrimaryInsurance, SyntheticCostEvidenceState.Missing);
        var current = Build(next, revision: 2);
        var r = Inspect(p, History(p, CostAwarenessChoice.AcknowledgedUncertainty), current);
        Assert.Equal(SyntheticCostChoiceContinuityState.CurrentMissingRequiresSeparateReview, r.State);
        Assert.Null(current.Lines[1].ClaimedAmount);
        Assert.Contains(SyntheticCostComponent.PrimaryInsurance, r.ChangedComponents);
        Assert.False(r.ChoiceCarriedForward);
    }

    [Fact]
    public void DisputedInsurerAmountCannotCarryPreviousNumericClaim()
    {
        var lines = Lines(); var old = Build(lines);
        var next = lines.ToArray();
        next[2] = Line(SyntheticCostComponent.SupplementalInsurance,
            SyntheticCostEvidenceState.ConflictingClaims);
        var current = Build(next, revision: 2);
        var r = Inspect(old, History(old, CostAwarenessChoice.RequestedClarification), current);
        Assert.Equal(SyntheticCostChoiceContinuityState.CurrentConflictRequiresSeparateReview, r.State);
        Assert.Null(current.Lines[2].ClaimedAmount);
        Assert.Contains(SyntheticCostComponent.SupplementalInsurance, r.ChangedComponents);
    }

    [Fact]
    public void NoPriorChoiceIsNotImputedToAnUnverifiedPreview()
    {
        var p = Build(Lines());
        var r = Inspect(p, History(p), p);
        Assert.Equal(SyntheticCostChoiceContinuityState.NoPriorChoiceToCarry, r.State);
        Assert.Null(r.PreviousUnverifiedChoice);
        Assert.Equal(0, r.PreviousUnverifiedChoiceRevision);
    }

    [Fact]
    public void LastChoiceIsNotAClinicalConsentEvenWithMultipleObservations()
    {
        var p = Build(Lines());
        var h = History(p, CostAwarenessChoice.Deferred,
            CostAwarenessChoice.RequestedClarification,
            CostAwarenessChoice.AcknowledgedUncertainty);
        var result = Inspect(p, h, p);
        Assert.Equal(3, result.PreviousUnverifiedChoiceRevision);
        Assert.Equal(CostAwarenessChoice.AcknowledgedUncertainty,
            result.PreviousUnverifiedChoice);
        Assert.False(result.ChoiceCarriedForward);
        Assert.False(result.FinancialCommitmentCreated);
        Assert.False(result.ClinicalConsentCreated);
    }

    [Fact]
    public void CrossMemberHistoryAndCurrentPreviewAreSeparateDenials()
    {
        var p = Build(Lines());
        var other = Build(p.Lines.ToArray(), member: "test:other-member");
        Assert.Equal(SyntheticCostChoiceContinuityState.MemberScopeMismatch,
            Inspect(p, History(p, CostAwarenessChoice.Deferred), other).State);
        Assert.Equal(SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid,
            Inspect(p, History(other, CostAwarenessChoice.Deferred), p).State);
    }

    [Theory]
    [InlineData("wrong-member")]
    [InlineData("changed-preview")]
    [InlineData("changed-revision")]
    [InlineData("changed-fingerprint")]
    public void PinnedHistoricalChoiceMustMatchPriorActualPreview(string mutation)
    {
        var p = Build(Lines());
        var separate = mutation switch
        {
            "wrong-member" => Build(p.Lines.ToArray(), member: "test:other-member"),
            "changed-preview" => Build(p.Lines.ToArray(), id: Guid.NewGuid()),
            "changed-revision" => Build(p.Lines.ToArray(), revision: 2),
            "changed-fingerprint" => Build(
                p.Lines.Select((x,i) => i == 0 ? x with { ClaimedAmount = 14m } : x)
                    .ToArray()),
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid,
            Inspect(p, History(separate, CostAwarenessChoice.Deferred), p).State);
    }

    [Fact]
    public void HistoricalChoiceOnOldVersionMustNotBeReplayedAgainstChangedPreview()
    {
        var p = Build(Lines());
        var h = History(p, CostAwarenessChoice.AcknowledgedUncertainty);
        var current = Build(p.Lines.ToArray(), revision: 3);
        var result = Inspect(p, h, current);
        Assert.Equal(SyntheticCostChoiceContinuityState.ChangedPreviewRequiresSeparateReview,
            result.State);
        Assert.Equal(CostAwarenessChoice.AcknowledgedUncertainty,
            result.PreviousUnverifiedChoice);
    }

    [Theory]
    [InlineData("default-old")]
    [InlineData("default-current")]
    [InlineData("fabricated-hash-old")]
    [InlineData("fabricated-hash-current")]
    [InlineData("invalid-current")]
    public void InvalidCostSourcesAreNeverDisplayedAsAvailable(string mode)
    {
        var p = Build(Lines());
        var now = p;
        if (mode == "default-old") p = default;
        if (mode == "default-current") now = default;
        if (mode == "fabricated-hash-old") p = p with { Fingerprint = new string('A', 64) };
        if (mode == "fabricated-hash-current") now = now with { Fingerprint = new string('A', 64) };
        if (mode == "invalid-current") now = now with { State = SyntheticCostPreviewState.AllComponentsProvisionalOnly,
            Lines = ImmutableArray<SyntheticCostLine>.Empty };
        var inspected = Inspect(p, History(Build(Lines()), CostAwarenessChoice.Deferred), now);
        Assert.Equal(SyntheticCostChoiceContinuityState.InvalidSource, inspected.State);
        Assert.Null(inspected.PreviousUnverifiedChoice);
        Assert.False(inspected.ChoiceCarriedForward);
    }

    [Fact]
    public void NullHistoryFailsClosedBeforeAnyPriorChoiceIsAssumed()
    {
        var p = Build(Lines());
        Assert.Equal(SyntheticCostChoiceContinuityState.PreviousChoiceHistoryInvalid,
            SyntheticCostChoiceContinuity.Inspect(p, null, p).State);
    }

    [Fact]
    public void NonBindingResponseCannotImplyFinancialOrMedicalAction()
    {
        var names = typeof(SyntheticCostChoiceContinuityRead).GetProperties()
            .Select(x => x.Name).ToArray();
        Assert.DoesNotContain(names, x => new[] { "PaymentAuthorized",
            "WalletReserved", "MedicalConsentGranted", "PatientData",
            "Signature", "LegalCommitmentId", "InsurerApproved" }.Contains(x));
        var p = Build(Lines());
        var r = Inspect(p, History(p, CostAwarenessChoice.Deferred), p);
        Assert.False(r.FinancialCommitmentCreated);
        Assert.False(r.ClinicalConsentCreated);
    }
}
