using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticInsuranceDisputeTrailTests
{
    private const string Member = "test:member-a";
    private const string Case = "test:insurance-case-a";
    private const string Treatment = "test:treatment-a";
    private static readonly Guid Packet = Guid.Parse("00000000-0000-4000-8000-000000000048");

    private static SyntheticSupportSnapshot Support(
        SyntheticSupportTopic topic = SyntheticSupportTopic.InsuranceCoverageDispute)
    {
        var cmd = new SyntheticSupportCommand(
            Guid.NewGuid(), 0, SyntheticSupportAction.SubmitUnverified,
            Case, Member, topic, "test:insurance-case-evidence", "test:member-claim",
            Guid.Empty, null, null);
        var result = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, cmd);
        Assert.Equal(SyntheticSupportOutcome.RecordedUnverified, result.Outcome);
        return result.Snapshot;
    }

    private static SyntheticBillEvidence[] Bill(
        SyntheticBillLane lane = SyntheticBillLane.PrimaryInsurance)
    {
        var claimedIssuer = lane switch
        {
            SyntheticBillLane.PrimaryInsurance => SyntheticBillClaimedIssuer.PrimaryInsurer,
            SyntheticBillLane.SupplementalInsurance =>
                SyntheticBillClaimedIssuer.SupplementalInsurer,
            SyntheticBillLane.ProviderService =>
                SyntheticBillClaimedIssuer.ProviderDocumentIssuer,
            _ => SyntheticBillClaimedIssuer.LicensedCreditCustodian
        };
        var first = new SyntheticBillEvidence(Member, Treatment, Packet, lane,
            claimedIssuer, SyntheticBillEvidenceChange.UnverifiedClaim,
            Guid.NewGuid(), Guid.Empty, 1, "test:original-invoice-paper", 1, 10m, "IRR");
        var disputed = first with {
            EvidenceId = Guid.NewGuid(), SupersedesEvidenceId = first.EvidenceId,
            LaneRevision = 2, SourceRevision = 2,
            Change = SyntheticBillEvidenceChange.Disputed,
            UnverifiedAmount = null, CurrencyCode = null,
            UnverifiedSourceRef = "test:dispute-paper"
        };
        return [first, disputed];
    }

    private static SyntheticInsuranceHandoffCommand Root(
        SyntheticBillEvidence[] bill,
        SyntheticInsuranceRoute route = SyntheticInsuranceRoute.DirectLicensedInsurer,
        Guid? id = null, long revision = 0) =>
        new(id ?? Guid.NewGuid(), revision,
            SyntheticInsuranceHandoffAction.OpenUnverifiedInsurerDisputeScope,
            Case, Member, Treatment, Packet, bill[^1].Lane, bill[^1].EvidenceId,
            bill[^1].LaneRevision, route, "test:claimed-insurer-a",
            route == SyntheticInsuranceRoute.LicensedIntermediary
                ? "test:claimed-intermediary-a" : null,
            Guid.Empty, Guid.Empty, null, null);

    private static SyntheticInsuranceHandoffCommand Follow(
        SyntheticInsuranceHandoffCommand root,
        SyntheticInsuranceHandoffAction action, long revision,
        Guid forward = default, Guid? id = null) =>
        root with {
            RequestId = id ?? Guid.NewGuid(),
            ExpectedRevision = revision,
            Action = action,
            OriginalScopeRequestId = root.RequestId,
            OriginalForwardRequestId = forward,
            ClaimedActorRef = action ==
                SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest
                ? "test:claimed-support-actor" : "test:claimed-other-actor",
            UnverifiedEvidenceRef = "test:opaque-paper-ref"
        };

    private static SyntheticInsuranceHandoffChange Apply(
        SyntheticInsuranceHandoffSnapshot? snap,
        SyntheticInsuranceHandoffCommand command,
        SyntheticSupportSnapshot support,
        SyntheticBillEvidence[] bill, long billRevision = 1) =>
        SyntheticInsuranceDisputeTrail.Apply(
            snap, command, support, billRevision, bill);

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void InsuranceDisputeScopesCanOnlyBeReadAsUnverifiedEvidence(
        int lane, int route)
    {
        var bill = Bill((SyntheticBillLane)lane);
        var root = Root(bill, (SyntheticInsuranceRoute)route);
        var result = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, Support(), bill);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.RecordedUnverified, result.Outcome);
        var read = SyntheticInsuranceDisputeProjection.Project(result.Snapshot)!.Value;
        Assert.Equal(1, read.Revision);
        Assert.Equal((SyntheticBillLane)lane, read.DisputedInsurerLane);
        Assert.False(read.UnverifiedForwardNotePresent);
        Assert.False(read.UnverifiedReceiptNotePresent);
        Assert.False(read.UnverifiedResponseSourceNotePresent);
    }

    [Fact]
    public void PaperForwardAndReceiptAndResponseAreThreeDistinctUntrustedStages()
    {
        var bill = Bill();
        var support = Support();
        var root = Root(bill);
        var one = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        var forward = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 1);
        var two = Apply(one.Snapshot, forward, support, bill);
        var a = SyntheticInsuranceDisputeProjection.Project(two.Snapshot)!.Value;
        Assert.True(a.UnverifiedForwardNotePresent);
        Assert.False(a.UnverifiedReceiptNotePresent);
        var recv = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt,
            2, forward.RequestId);
        var three = Apply(two.Snapshot, recv, support, bill);
        var response = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerResponseSource,
            3, forward.RequestId);
        var four = Apply(three.Snapshot, response, support, bill);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.RecordedUnverified, four.Outcome);
        var result = SyntheticInsuranceDisputeProjection.Project(four.Snapshot)!.Value;
        Assert.Equal(4, result.Revision);
        Assert.True(result.UnverifiedForwardNotePresent);
        Assert.True(result.UnverifiedReceiptNotePresent);
        Assert.True(result.UnverifiedResponseSourceNotePresent);
        Assert.Equal(SyntheticBillViewState.Disputed,
            SyntheticTreatmentBillingAssembler.Build(Member, Treatment, Packet, 1, bill).State);
    }

    [Fact]
    public void HistoricalReplaysAreIdempotentAndNeverAdvanceLedger()
    {
        var bill = Bill();
        var support = Support();
        var root = Root(bill);
        var one = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        var forward = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 1);
        var two = Apply(one.Snapshot, forward, support, bill);
        var receipt = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt,
            2, forward.RequestId);
        var three = Apply(two.Snapshot, receipt, support, bill);
        foreach (var cmd in new[] { root, forward, receipt })
        {
            var replay = Apply(three.Snapshot, cmd, support, bill);
            Assert.Equal(SyntheticInsuranceHandoffOutcome.Replayed, replay.Outcome);
            Assert.Same(three.Snapshot, replay.Snapshot);
        }
    }

    [Theory]
    [InlineData("member")]
    [InlineData("case")]
    [InlineData("treatment")]
    [InlineData("packet")]
    [InlineData("lane")]
    [InlineData("evidence")]
    [InlineData("revision")]
    [InlineData("insurer")]
    [InlineData("route")]
    [InlineData("intermediary")]
    [InlineData("origin")]
    public void CannotCrossCaseMemberIssuerOrPinnedBillScope(string mode)
    {
        var bill = Bill();
        var support = Support();
        var root = Root(bill);
        var opened = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        var cmd = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 1);
        cmd = mode switch
        {
            "member" => cmd with { MemberRef = "test:other-member" },
            "case" => cmd with { CaseRef = "test:other-case" },
            "treatment" => cmd with { TreatmentRef = "test:other-treatment" },
            "packet" => cmd with { BillPacketId = Guid.NewGuid() },
            "lane" => cmd with { DisputedInsurerLane = SyntheticBillLane.SupplementalInsurance },
            "evidence" => cmd with { DisputedEvidenceId = Guid.NewGuid() },
            "revision" => cmd with { DisputedLaneRevision = 1 },
            "insurer" => cmd with { ClaimedInsurerRef = "test:other-insurer" },
            "route" => cmd with { Route = SyntheticInsuranceRoute.LicensedIntermediary,
                ClaimedIntermediaryRef = "test:new-intermediary" },
            "intermediary" => cmd with { ClaimedIntermediaryRef = "test:new-intermediary" },
            "origin" => cmd with { OriginalScopeRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        Assert.NotEqual(SyntheticInsuranceHandoffOutcome.RecordedUnverified,
            Apply(opened.Snapshot, cmd, support, bill).Outcome);
    }

    [Theory]
    [InlineData("real-member")]
    [InlineData("real-case")]
    [InlineData("bad-treatment")]
    [InlineData("missing-id")]
    [InlineData("zero-revision")]
    [InlineData("invalid-lane")]
    [InlineData("invalid-route")]
    [InlineData("invalid-action")]
    [InlineData("bad-insurer")]
    [InlineData("direct-with-middleman")]
    [InlineData("indirect-without-middleman")]
    [InlineData("root-with-actor")]
    [InlineData("root-with-forward")]
    public void MalformedCaseFailsClosed(string mode)
    {
        var bill = Bill();
        var root = Root(bill);
        root = mode switch
        {
            "real-member" => root with { MemberRef = "real:member" },
            "real-case" => root with { CaseRef = "real:case" },
            "bad-treatment" => root with { TreatmentRef = "" },
            "missing-id" => root with { RequestId = Guid.Empty },
            "zero-revision" => root with { DisputedLaneRevision = 0 },
            "invalid-lane" => root with { DisputedInsurerLane = (SyntheticBillLane)99 },
            "invalid-route" => root with { Route = (SyntheticInsuranceRoute)99 },
            "invalid-action" => root with { Action = (SyntheticInsuranceHandoffAction)99 },
            "bad-insurer" => root with { ClaimedInsurerRef = "insurer:live" },
            "direct-with-middleman" => root with { ClaimedIntermediaryRef = "test:middleman" },
            "indirect-without-middleman" => root with {
                Route = SyntheticInsuranceRoute.LicensedIntermediary },
            "root-with-actor" => root with { ClaimedActorRef = "test:actor" },
            "root-with-forward" => root with { OriginalForwardRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticInsuranceHandoffOutcome.Invalid,
            Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, Support(), bill).Outcome);
    }

    [Theory]
    [InlineData("medical-case")]
    [InlineData("missing-support")]
    [InlineData("wrong-support-member")]
    [InlineData("undisputed-bill")]
    [InlineData("provider-invoice-lane")]
    [InlineData("wrong-bill-id")]
    [InlineData("wrong-bill-revision")]
    [InlineData("wrong-packet")]
    public void UnsupportedIntakeOrInsurerLaneCannotOpen(string mode)
    {
        var bill = Bill();
        var support = Support(mode == "medical-case"
            ? SyntheticSupportTopic.MedicalServiceComplaint :
              SyntheticSupportTopic.InsuranceCoverageDispute);
        var root = Root(bill);
        if (mode == "wrong-support-member") root = root with { MemberRef = "test:other-person" };
        if (mode == "wrong-bill-id") root = root with { DisputedEvidenceId = Guid.NewGuid() };
        if (mode == "wrong-bill-revision") root = root with { DisputedLaneRevision = 1 };
        if (mode == "wrong-packet") root = root with { BillPacketId = Guid.NewGuid() };
        if (mode == "undisputed-bill")
            bill = bill[..1];
        if (mode == "provider-invoice-lane")
        {
            bill = Bill(SyntheticBillLane.ProviderService);
            root = Root(bill);
        }
        var response = SyntheticInsuranceDisputeTrail.Apply(
            SyntheticInsuranceHandoffSnapshot.Empty, root,
            mode == "missing-support" ? null : support, 1, bill);
        Assert.NotEqual(SyntheticInsuranceHandoffOutcome.RecordedUnverified, response.Outcome);
    }

    [Fact]
    public void NoForwardMeansReceiptOrResponseCannotBeInvented()
    {
        var bill = Bill();
        var support = Support();
        var root = Root(bill);
        var opened = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        var receipt = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt, 1, Guid.NewGuid());
        Assert.Equal(SyntheticInsuranceHandoffOutcome.PreviousStageMissing,
            Apply(opened.Snapshot, receipt, support, bill).Outcome);
        var response = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerResponseSource, 1, Guid.NewGuid());
        Assert.Equal(SyntheticInsuranceHandoffOutcome.PreviousStageMissing,
            Apply(opened.Snapshot, response, support, bill).Outcome);
    }

    [Fact]
    public void InsurerResponseCannotBeRecordedBeforeSeparateReceiptClaim()
    {
        var bill = Bill(); var support = Support(); var root = Root(bill);
        var opened = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        var fwd = Follow(root, SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 1);
        var sent = Apply(opened.Snapshot, fwd, support, bill);
        var next = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerResponseSource, 2, fwd.RequestId);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.PreviousStageMissing,
            Apply(sent.Snapshot, next, support, bill).Outcome);
    }

    [Fact]
    public void WrongOrSameActorCannotImpersonateInsurerReceipt()
    {
        var bill = Bill(); var support = Support(); var root = Root(bill);
        var opened = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        var forward = Follow(root, SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 1);
        var sent = Apply(opened.Snapshot, forward, support, bill);
        var receive = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt, 2, forward.RequestId)
            with { ClaimedActorRef = forward.ClaimedActorRef };
        Assert.Equal(SyntheticInsuranceHandoffOutcome.ClaimedRecipientMismatch,
            Apply(sent.Snapshot, receive, support, bill).Outcome);
    }

    [Theory]
    [InlineData("new-view-revision")]
    [InlineData("changed-source")]
    [InlineData("support-note")]
    public void ChangedSourcesCannotSilentlyUpgradePinnedDispute(string mode)
    {
        var bill = Bill(); var support = Support(); var root = Root(bill);
        var opened = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        long viewRevision = 1;
        if (mode == "new-view-revision") viewRevision = 2;
        if (mode == "changed-source")
            bill[0] = bill[0] with { UnverifiedAmount = 11m };
        if (mode == "support-note")
        {
            var original = support.History[0].Command;
            var more = original with {
                RequestId = Guid.NewGuid(), ExpectedRevision = 1,
                Action = SyntheticSupportAction.RecordUnverifiedFollowUp,
                OriginalSubmissionRequestId = original.RequestId,
                ClaimedFollowUpActorRef = "test:claimed-coordinator",
                UnverifiedFollowUpNoteRef = "test:paper-note"
            };
            support = SyntheticSupportIntake.Apply(support, more).Snapshot;
        }
        var attempt = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 1);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.SourceChanged,
            Apply(opened.Snapshot, attempt, support, bill, viewRevision).Outcome);
    }

    [Fact]
    public void StaleCaseRevisionAndChangedReplayDoNotRewriteCase()
    {
        var bill = Bill(); var support = Support(); var root = Root(bill);
        var one = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        var forward = Follow(root, SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 1);
        var two = Apply(one.Snapshot, forward, support, bill);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.StaleRevision,
            Apply(two.Snapshot, Follow(root,
                SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt, 1,
                forward.RequestId), support, bill).Outcome);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.ConflictingReplay,
            Apply(two.Snapshot, forward with { ClaimedActorRef = "test:wrong" },
                support, bill).Outcome);
    }

    [Fact]
    public void DuplicatePaperForwardOrReceiptIsNotANewLegalDecision()
    {
        var bill = Bill(); var support = Support(); var root = Root(bill);
        var one = Apply(SyntheticInsuranceHandoffSnapshot.Empty, root, support, bill);
        var first = Follow(root, SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 1);
        var two = Apply(one.Snapshot, first, support, bill);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.DuplicateStage,
            Apply(two.Snapshot, Follow(root, SyntheticInsuranceHandoffAction.RecordUnverifiedForwardRequest, 2),
                support, bill).Outcome);
        var receipt = Follow(root,
            SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt, 2, first.RequestId);
        var three = Apply(two.Snapshot, receipt, support, bill);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.DuplicateStage,
            Apply(three.Snapshot, Follow(root,
                SyntheticInsuranceHandoffAction.RecordUnverifiedInsurerReceipt, 3, first.RequestId),
                support, bill).Outcome);
    }

    [Fact]
    public void InvalidOrMissingSnapshotCannotAuthorizeAnyMoneyOrInsuranceCoverage()
    {
        var bill = Bill(); var support = Support(); var root = Root(bill);
        Assert.Equal(SyntheticInsuranceHandoffOutcome.Invalid,
            Apply(null, root, support, bill).Outcome);
        Assert.Null(SyntheticInsuranceDisputeProjection.Project(null));
        Assert.Null(SyntheticInsuranceDisputeProjection.Project(SyntheticInsuranceHandoffSnapshot.Empty));
        var names = typeof(SyntheticInsuranceHandoffRead).GetProperties().Select(p => p.Name);
        foreach (var key in new[] { "Approved", "Coverage", "Settled", "Paid", "Amount",
            "Compensation", "Wallet", "Debit", "Clinically", "LegallyFinal" })
            Assert.DoesNotContain(names, x => x.Contains(key, StringComparison.OrdinalIgnoreCase));
    }
}
