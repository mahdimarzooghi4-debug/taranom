using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticCostPreviewTests
{
    private const string Member = "test:member-one";
    private static readonly Guid PreviewId =
        Guid.Parse("00000000-0000-4000-8000-000000000028");

    private static SyntheticCostLine Missing(SyntheticCostComponent slot) =>
        new(slot, SyntheticCostEvidenceState.Missing, null, null, null, Guid.Empty, 0);

    private static SyntheticCostLine Unverified(SyntheticCostComponent slot,
        decimal amount = 42, string currency = "IRR") =>
        new(slot, SyntheticCostEvidenceState.ProvisionalClaim, amount,
            currency, "test:unverified-source", Guid.NewGuid(), 1);

    private static SyntheticCostLine Conflict(SyntheticCostComponent slot) =>
        new(slot, SyntheticCostEvidenceState.ConflictingClaims,
            null, null, "test:disputed-evidence", Guid.NewGuid(), 2);

    private static SyntheticCostLine[] MissingAll() =>
        Enum.GetValues<SyntheticCostComponent>().Select(Missing).ToArray();

    private static SyntheticCostPreview Build(
        SyntheticCostLine[]? lines, long revision = 1,
        string? member = Member, Guid? preview = null) =>
        SyntheticCostPreviewBuilder.Build(member, preview ?? PreviewId, revision, lines);

    [Fact]
    public void FiveAbsentComponentsStayUnknownNotZero()
    {
        var result = Build(MissingAll());
        Assert.Equal(SyntheticCostPreviewState.MissingOrIncomplete, result.State);
        Assert.Equal(5, result.Lines.Length);
        Assert.All(result.Lines, x => {
            Assert.Null(x.ClaimedAmount);
            Assert.Equal(SyntheticCostEvidenceState.Missing, x.State);
        });
        Assert.Equal(64, result.Fingerprint!.Length);
    }

    [Fact]
    public void ExplicitUnverifiedZeroIsPreservedAndNeverTreatedAsMissing()
    {
        var lines = MissingAll();
        lines[0] = Unverified(SyntheticCostComponent.ProviderService, 0);
        var result = Build(lines);
        Assert.Equal(0m, result.Lines[0].ClaimedAmount);
        Assert.Equal(SyntheticCostEvidenceState.ProvisionalClaim, result.Lines[0].State);
        Assert.Null(result.Lines[1].ClaimedAmount);
        Assert.Equal(SyntheticCostPreviewState.MissingOrIncomplete, result.State);
    }

    [Fact]
    public void AllFivePresentRemainProvisionalNeverOfficialOrFinal()
    {
        var result = Build(Enum.GetValues<SyntheticCostComponent>()
            .Select((slot, index) => Unverified(slot, index + 1)).ToArray());
        Assert.Equal(SyntheticCostPreviewState.AllComponentsProvisionalOnly, result.State);
        Assert.DoesNotContain(Enum.GetNames<SyntheticCostPreviewState>(),
            s => s.Contains("Approved") || s.Contains("Final") || s.Contains("Payable"));
    }

    [Fact]
    public void ConflictedComponentHasNoClaimedNumber()
    {
        var items = MissingAll();
        items[1] = Conflict(SyntheticCostComponent.PrimaryInsurance);
        var result = Build(items);
        Assert.Equal(SyntheticCostPreviewState.Conflicted, result.State);
        Assert.Null(result.Lines[1].ClaimedAmount);
    }

    [Theory]
    [InlineData("negative-amount")]
    [InlineData("source-unscoped")]
    [InlineData("no-evidence")]
    [InlineData("zero-revision")]
    [InlineData("missing-has-value")]
    [InlineData("conflict-has-value")]
    [InlineData("wrong-currency")]
    [InlineData("invalid-state")]
    public void InvalidComponentMetadataFailsClosed(string mutation)
    {
        var items = MissingAll();
        var line = Unverified(SyntheticCostComponent.ProviderService, 1);
        items[0] = mutation switch
        {
            "negative-amount" => line with { ClaimedAmount = -1m },
            "source-unscoped" => line with { UnverifiedSourceRef = "actual-provider" },
            "no-evidence" => line with { EvidenceRef = Guid.Empty },
            "zero-revision" => line with { SourceRevision = 0 },
            "missing-has-value" => Missing(SyntheticCostComponent.ProviderService)
                with { ClaimedAmount = 0m },
            "conflict-has-value" => Conflict(SyntheticCostComponent.ProviderService)
                with { ClaimedAmount = 15m },
            "wrong-currency" => line with { CurrencyCode = "rials" },
            "invalid-state" => line with { State = (SyntheticCostEvidenceState)999 },
            _ => throw new ArgumentException("Unknown test.")
        };
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(items).State);
    }

    [Fact]
    public void DifferentCurrenciesCannotBeCombinedIntoAFalseAggregate()
    {
        var items = MissingAll();
        items[0] = Unverified(SyntheticCostComponent.ProviderService, 12, "IRR");
        items[1] = Unverified(SyntheticCostComponent.PrimaryInsurance, 2, "USD");
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(items).State);
    }

    [Fact]
    public void MissingDuplicateOrUnknownComponentNeverProducesPreview()
    {
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(MissingAll()[..4]).State);
        var items = MissingAll();
        items[4] = Missing(SyntheticCostComponent.ProviderService);
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(items).State);
        items[4] = Missing((SyntheticCostComponent)99);
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(items).State);
    }

    [Fact]
    public void MemberOrRevisionOrPreviewMustHaveSyntheticIdentity()
    {
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(MissingAll(), member: "real:member").State);
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(MissingAll(), revision: 0).State);
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(MissingAll(), preview: Guid.Empty).State);
        Assert.Equal(SyntheticCostPreviewState.Invalid, Build(null).State);
    }

    [Fact]
    public void CanonicalFingerprintIgnoresOrderButPinsVersionAndSources()
    {
        var items = MissingAll();
        items[2] = Unverified(SyntheticCostComponent.SupplementalInsurance, 0);
        var one = Build(items);
        Assert.Equal(one.Fingerprint, Build(items.Reverse().ToArray()).Fingerprint);
        Assert.NotEqual(one.Fingerprint, Build(items, revision: 2).Fingerprint);
        items[2] = items[2] with { SourceRevision = 2 };
        Assert.NotEqual(one.Fingerprint, Build(items).Fingerprint);
    }

    [Fact]
    public void FingerprintSeparatesMissingFromExplicitClaimedZero()
    {
        var unknown = MissingAll();
        var amountZero = MissingAll();
        amountZero[0] = Unverified(SyntheticCostComponent.ProviderService, 0);
        Assert.NotEqual(Build(unknown).Fingerprint, Build(amountZero).Fingerprint);
    }

    [Fact]
    public void PresentationContractDoesNotCalculateAggregateOrPayment()
    {
        Assert.DoesNotContain(typeof(SyntheticCostPreview).GetProperties(),
            p => p.Name.Contains("Total") || p.Name.Contains("Payable") ||
                 p.Name.Contains("Authorized") || p.Name.Contains("Approved"));
    }
}

public sealed class CostAwarenessRecorderTests
{
    private static readonly Guid PreviewId =
        Guid.Parse("00000000-0000-4000-8000-000000000028");
    private const string Member = "test:member-one";

    private static SyntheticCostPreview NewPreview(long revision = 1) =>
        SyntheticCostPreviewBuilder.Build(Member, PreviewId, revision,
            Enum.GetValues<SyntheticCostComponent>()
                .Select(slot => new SyntheticCostLine(
                    slot, SyntheticCostEvidenceState.Missing, null, null,
                    null, Guid.Empty, 0)).ToArray());

    private static CostAwarenessCommand Command(SyntheticCostPreview preview,
        Guid? request = null, long expected = 0,
        CostAwarenessChoice choice = CostAwarenessChoice.Deferred) =>
        new(request ?? Guid.NewGuid(), expected, preview.MemberRef!,
            preview.PreviewId, preview.PreviewRevision, preview.Fingerprint!, choice);

    [Fact]
    public void ClaimedChoiceAppendsButNoLegalOrFinancialApprovalExists()
    {
        var preview = NewPreview();
        var snapshot = CostAwarenessSnapshot.FromPreview(preview);
        Assert.NotNull(snapshot);
        var command = Command(preview);
        var change = CostAwarenessRecorder.Record(snapshot, command);
        Assert.Equal(CostAwarenessOutcome.RecordedUnverified, change.Outcome);
        Assert.Equal(1, change.Snapshot!.Revision);
        Assert.Single(change.Snapshot.History);
        Assert.DoesNotContain(Enum.GetNames<CostAwarenessOutcome>(),
            text => text.Contains("Paid") || text.Contains("Approved") ||
                    text.Contains("Consent") || text.Contains("Authorized"));
    }

    [Fact]
    public void SubsequentNewChoiceCannotReuseStaleRevision()
    {
        var preview = NewPreview();
        var initial = CostAwarenessSnapshot.FromPreview(preview);
        var result = CostAwarenessRecorder.Record(initial, Command(preview));
        var stale = CostAwarenessRecorder.Record(result.Snapshot, Command(preview));
        Assert.Equal(CostAwarenessOutcome.StaleRevision, stale.Outcome);
        Assert.Same(result.Snapshot, stale.Snapshot);
    }

    [Fact]
    public void ExactReplayAfterLaterEventReturnsCurrentSnapshot()
    {
        var preview = NewPreview();
        var firstCmd = Command(preview);
        var first = CostAwarenessRecorder.Record(CostAwarenessSnapshot.FromPreview(preview), firstCmd);
        var second = CostAwarenessRecorder.Record(first.Snapshot,
            Command(preview, expected: 1, choice: CostAwarenessChoice.RequestedClarification));
        Assert.Equal(CostAwarenessOutcome.Replayed,
            CostAwarenessRecorder.Record(second.Snapshot, firstCmd).Outcome);
        Assert.Equal(2, second.Snapshot!.Revision);
    }

    [Fact]
    public void DifferentPayloadWithSameRequestIdIsConflictingReplay()
    {
        var preview = NewPreview();
        var original = Command(preview);
        var first = CostAwarenessRecorder.Record(
            CostAwarenessSnapshot.FromPreview(preview), original);
        var conflict = CostAwarenessRecorder.Record(first.Snapshot,
            original with { Choice = CostAwarenessChoice.AcknowledgedUncertainty });
        Assert.Equal(CostAwarenessOutcome.ConflictingReplay, conflict.Outcome);
    }

    [Theory]
    [InlineData("different-member")]
    [InlineData("different-preview")]
    [InlineData("changed-revision")]
    [InlineData("changed-fingerprint")]
    public void CrossMemberAndChangedEstimateCannotAppend(string variant)
    {
        var preview = NewPreview();
        var initial = CostAwarenessSnapshot.FromPreview(preview);
        var command = Command(preview);
        command = variant switch
        {
            "different-member" => command with { MemberRef = "test:someone-else" },
            "different-preview" => command with { PreviewId = Guid.NewGuid() },
            "changed-revision" => command with { PreviewRevision = 2 },
            "changed-fingerprint" => command with { PreviewFingerprint = new string('A', 64) },
            _ => throw new ArgumentException()
        };
        var result = CostAwarenessRecorder.Record(initial, command);
        Assert.Equal(variant.StartsWith("different")
            ? CostAwarenessOutcome.ScopeMismatch : CostAwarenessOutcome.PreviewChanged, result.Outcome);
        Assert.Empty(initial!.History);
    }

    [Theory]
    [InlineData("no-request")]
    [InlineData("unknown-choice")]
    [InlineData("real-member")]
    [InlineData("negative-revision")]
    public void MalformedChoiceCannotBeRecorded(string variant)
    {
        var preview = NewPreview();
        var command = Command(preview);
        command = variant switch
        {
            "no-request" => command with { RequestId = Guid.Empty },
            "unknown-choice" => command with { Choice = (CostAwarenessChoice)99 },
            "real-member" => command with { MemberRef = "real:user" },
            "negative-revision" => command with { ExpectedRevision = -1 },
            _ => throw new ArgumentException()
        };
        Assert.Equal(CostAwarenessOutcome.Invalid,
            CostAwarenessRecorder.Record(CostAwarenessSnapshot.FromPreview(preview), command).Outcome);
    }

    [Fact]
    public void InvalidPreviewCannotBecomeChoiceSnapshot()
    {
        Assert.Null(CostAwarenessSnapshot.FromPreview(default));
        var invalid = SyntheticCostPreviewBuilder.Build("real:member", Guid.NewGuid(), 1, null);
        Assert.Null(CostAwarenessSnapshot.FromPreview(invalid));
        Assert.Equal(CostAwarenessOutcome.Invalid,
            CostAwarenessRecorder.Record(null, Command(NewPreview())).Outcome);
    }

    [Fact]
    public void AllThreeObservationsAreNonBinding()
    {
        var preview = NewPreview();
        var snapshot = CostAwarenessSnapshot.FromPreview(preview);
        foreach (var kind in Enum.GetValues<CostAwarenessChoice>())
        {
            var change = CostAwarenessRecorder.Record(snapshot, Command(preview, choice: kind));
            Assert.Equal(CostAwarenessOutcome.RecordedUnverified, change.Outcome);
        }
    }
}
