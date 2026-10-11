using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticUrgentSafetyTrailTests
{
    private static SyntheticUrgentSafetyCommand Root(
        Guid? requestId = null, long revision = 0) =>
        new(requestId ?? Guid.NewGuid(), revision,
            SyntheticUrgentSafetyAction.OpenUnverifiedConcernContext,
            "test:case-a", "test:member-a", "test:unverified-concern-a",
            Guid.Empty, null, null, Guid.Empty, null, null);

    private static SyntheticUrgentSafetyCommand Warning(
        SyntheticUrgentSafetyCommand root, long revision = 1,
        Guid? requestId = null) =>
        root with
        {
            RequestId = requestId ?? Guid.NewGuid(),
            ExpectedRevision = revision,
            Action = SyntheticUrgentSafetyAction.RecordUnverifiedWarningEvidence,
            OriginalContextRequestId = root.RequestId,
            ClaimedActorRef = "test:claimed-warning-author",
            UnverifiedEvidenceRef = "test:warning-paper-reference"
        };

    private static SyntheticUrgentSafetyCommand Request(
        SyntheticUrgentSafetyCommand root, long revision = 1,
        Guid? requestId = null) =>
        root with
        {
            RequestId = requestId ?? Guid.NewGuid(),
            ExpectedRevision = revision,
            Action = SyntheticUrgentSafetyAction.RecordUnverifiedReferralRequestEvidence,
            OriginalContextRequestId = root.RequestId,
            ClaimedActorRef = "test:claimed-coordinator",
            UnverifiedEvidenceRef = "test:outbound-request-paper",
            ClaimedClinicalTeamRef = "test:claimed-team",
            UnverifiedTeamAuthorityEvidenceRef = "test:unverified-team-authority"
        };

    private static SyntheticUrgentSafetyCommand Receipt(
        SyntheticUrgentSafetyCommand root,
        SyntheticUrgentSafetyCommand request,
        long revision = 2, Guid? requestId = null) =>
        request with
        {
            RequestId = requestId ?? Guid.NewGuid(),
            ExpectedRevision = revision,
            Action = SyntheticUrgentSafetyAction.RecordUnverifiedClinicalReceiptEvidence,
            OriginalReferralRequestId = request.RequestId,
            ClaimedActorRef = "test:claimed-receiving-clinician",
            UnverifiedEvidenceRef = "test:receiver-paper-evidence"
        };

    private static SyntheticUrgentSafetyChange Apply(
        SyntheticUrgentSafetySnapshot snapshot, SyntheticUrgentSafetyCommand command) =>
        SyntheticUrgentSafetyTrail.Apply(snapshot, command);

    [Fact]
    public void AConcernContextIsNotAnEmergencyDiagnosis()
    {
        var result = Apply(SyntheticUrgentSafetySnapshot.Empty, Root());
        Assert.Equal(SyntheticUrgentSafetyOutcome.RecordedUnverified, result.Outcome);
        var read = SyntheticUrgentSafetyProjection.Project(result.Snapshot)!.Value;
        Assert.Equal(1, read.Revision);
        Assert.False(read.UnverifiedWarningNotePresent);
        Assert.False(read.UnverifiedReferralRequestNotePresent);
        Assert.False(read.UnverifiedRecipientReceiptNotePresent);
    }

    [Fact]
    public void AWarningPaperNoteNeverImpliesClinicianReferralOrReceipt()
    {
        var root = Root();
        var one = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var two = Apply(one.Snapshot, Warning(root));
        Assert.Equal(SyntheticUrgentSafetyOutcome.RecordedUnverified, two.Outcome);
        var read = SyntheticUrgentSafetyProjection.Project(two.Snapshot)!.Value;
        Assert.True(read.UnverifiedWarningNotePresent);
        Assert.False(read.UnverifiedReferralRequestNotePresent);
        Assert.False(read.UnverifiedRecipientReceiptNotePresent);
    }

    [Fact]
    public void ProposedReferralIsIndependentOfWarningAndDoesNotImplyDelivery()
    {
        var root = Root();
        var one = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var two = Apply(one.Snapshot, Request(root));
        var read = SyntheticUrgentSafetyProjection.Project(two.Snapshot)!.Value;
        Assert.False(read.UnverifiedWarningNotePresent);
        Assert.True(read.UnverifiedReferralRequestNotePresent);
        Assert.False(read.UnverifiedRecipientReceiptNotePresent);
    }

    [Fact]
    public void DistinctReceiptPaperNoteNeedsExactProposedRequestAndTeam()
    {
        var root = Root();
        var one = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var warn = Apply(one.Snapshot, Warning(root));
        var request = Request(root, 2);
        var sent = Apply(warn.Snapshot, request);
        var received = Apply(sent.Snapshot, Receipt(root, request, 3));
        Assert.Equal(SyntheticUrgentSafetyOutcome.RecordedUnverified, received.Outcome);
        Assert.Equal(4, received.Snapshot.Revision);
        var read = SyntheticUrgentSafetyProjection.Project(received.Snapshot)!.Value;
        Assert.True(read.UnverifiedWarningNotePresent);
        Assert.True(read.UnverifiedReferralRequestNotePresent);
        Assert.True(read.UnverifiedRecipientReceiptNotePresent);
    }

    [Fact]
    public void ReceiptCanBeClaimedBeforeWarningButWarningNeverWaitsForReceipt()
    {
        var root = Root();
        var opened = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var request = Request(root);
        var two = Apply(opened.Snapshot, request);
        var three = Apply(two.Snapshot, Receipt(root, request));
        var four = Apply(three.Snapshot, Warning(root, 3));
        Assert.Equal(SyntheticUrgentSafetyOutcome.RecordedUnverified, four.Outcome);
        Assert.True(SyntheticUrgentSafetyProjection.Project(four.Snapshot)!.Value
            .UnverifiedWarningNotePresent);
    }

    [Fact]
    public void EveryAcceptedOldCommandIsIdempotentAfterAllOtherEvidence()
    {
        var root = Root();
        var w = Warning(root);
        var req = Request(root, 2);
        var recv = Receipt(root, req, 3);
        var one = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var two = Apply(one.Snapshot, w);
        var three = Apply(two.Snapshot, req);
        var four = Apply(three.Snapshot, recv);
        foreach (var cmd in new[] { root, w, req, recv })
        {
            var replay = Apply(four.Snapshot, cmd);
            Assert.Equal(SyntheticUrgentSafetyOutcome.Replayed, replay.Outcome);
            Assert.Same(four.Snapshot, replay.Snapshot);
        }
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("evidence")]
    [InlineData("team")]
    [InlineData("authority")]
    [InlineData("case")]
    [InlineData("member")]
    [InlineData("action")]
    public void ChangedPayloadWithSameRequestUuidConflicts(string field)
    {
        var root = Root();
        var opened = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var request = Request(root);
        var saved = Apply(opened.Snapshot, request);
        var bad = field switch
        {
            "actor" => request with { ClaimedActorRef = "test:another-actor" },
            "evidence" => request with { UnverifiedEvidenceRef = "test:other-evidence" },
            "team" => request with { ClaimedClinicalTeamRef = "test:other-team" },
            "authority" => request with {
                UnverifiedTeamAuthorityEvidenceRef = "test:other-authority" },
            "case" => request with { CaseRef = "test:other-case" },
            "member" => request with { MemberRef = "test:other-member" },
            "action" => request with {
                Action = SyntheticUrgentSafetyAction.RecordUnverifiedWarningEvidence,
                ClaimedClinicalTeamRef = null,
                UnverifiedTeamAuthorityEvidenceRef = null },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticUrgentSafetyOutcome.ConflictingReplay,
            Apply(saved.Snapshot, bad).Outcome);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("member")]
    [InlineData("concern")]
    [InlineData("origin")]
    public void CrossContextEvidenceIsDenied(string field)
    {
        var root = Root();
        var opened = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var cmd = Warning(root);
        var changed = field switch
        {
            "case" => cmd with { CaseRef = "test:other-case" },
            "member" => cmd with { MemberRef = "test:other-member" },
            "concern" => cmd with { UnverifiedConcernRef = "test:other-concern" },
            "origin" => cmd with { OriginalContextRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticUrgentSafetyOutcome.ScopeMismatch,
            Apply(opened.Snapshot, changed).Outcome);
    }

    [Theory]
    [InlineData("team")]
    [InlineData("authority")]
    [InlineData("actor")]
    [InlineData("wrong-request")]
    public void ReceiptCannotBeReboundToAnUnrelatedTeam(string mode)
    {
        var root = Root();
        var opened = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var request = Request(root);
        var requested = Apply(opened.Snapshot, request);
        var receipt = Receipt(root, request);
        var changed = mode switch
        {
            "team" => receipt with { ClaimedClinicalTeamRef = "test:wrong-team" },
            "authority" => receipt with {
                UnverifiedTeamAuthorityEvidenceRef = "test:changed-evidence" },
            "actor" => receipt with { ClaimedActorRef = request.ClaimedActorRef },
            "wrong-request" => receipt with { OriginalReferralRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        var result = Apply(requested.Snapshot, changed);
        Assert.Equal(mode == "wrong-request"
            ? SyntheticUrgentSafetyOutcome.ReferralRequestMissing
            : SyntheticUrgentSafetyOutcome.ClaimedRecipientMismatch, result.Outcome);
    }

    [Fact]
    public void RecipientReceiptWithoutProposedReferralCannotBecomeRealDelivery()
    {
        var root = Root();
        var opened = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var fake = Receipt(root, Request(root));
        Assert.Equal(SyntheticUrgentSafetyOutcome.ReferralRequestMissing,
            Apply(opened.Snapshot, fake).Outcome);
    }

    [Fact]
    public void DuplicateWarningReferralAndReceiptPaperStagesAreNotNewOperations()
    {
        var root = Root();
        var one = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var two = Apply(one.Snapshot, Warning(root));
        Assert.Equal(SyntheticUrgentSafetyOutcome.DuplicatePaperStage,
            Apply(two.Snapshot, Warning(root, 2)).Outcome);
        var req = Request(root, 2);
        var three = Apply(two.Snapshot, req);
        Assert.Equal(SyntheticUrgentSafetyOutcome.DuplicatePaperStage,
            Apply(three.Snapshot, Request(root, 3)).Outcome);
        var four = Apply(three.Snapshot, Receipt(root, req, 3));
        Assert.Equal(SyntheticUrgentSafetyOutcome.DuplicatePaperStage,
            Apply(four.Snapshot, Receipt(root, req, 4)).Outcome);
    }

    [Fact]
    public void StaleRevisionAndSecondRootCannotMutateHistory()
    {
        var root = Root();
        var one = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        Assert.Equal(SyntheticUrgentSafetyOutcome.StaleRevision,
            Apply(one.Snapshot, Warning(root, 0)).Outcome);
        Assert.Equal(SyntheticUrgentSafetyOutcome.ScopeMismatch,
            Apply(one.Snapshot, Root(revision: 1)).Outcome);
    }

    [Fact]
    public void NoSafetyActionAppearsFromMissingContext()
    {
        var root = Root();
        var initial = SyntheticUrgentSafetySnapshot.Empty;
        Assert.Equal(SyntheticUrgentSafetyOutcome.OriginalContextMissing,
            Apply(initial, Warning(root, 0)).Outcome);
        Assert.Equal(SyntheticUrgentSafetyOutcome.OriginalContextMissing,
            Apply(initial, Request(root, 0)).Outcome);
        Assert.Equal(SyntheticUrgentSafetyOutcome.OriginalContextMissing,
            Apply(initial, Receipt(root, Request(root, 0), 0)).Outcome);
    }

    [Theory]
    [InlineData("real-member")]
    [InlineData("real-case")]
    [InlineData("real-concern")]
    [InlineData("missing-id")]
    [InlineData("negative-revision")]
    [InlineData("unknown-action")]
    [InlineData("invalid-ref-format")]
    [InlineData("unexpected-team-at-opening")]
    [InlineData("unexpected-actor-at-opening")]
    [InlineData("unexpected-referral-at-opening")]
    public void InvalidRootIsNotRecorded(string field)
    {
        var original = Root();
        var bad = field switch
        {
            "real-member" => original with { MemberRef = "real:patient" },
            "real-case" => original with { CaseRef = "real:case" },
            "real-concern" => original with { UnverifiedConcernRef = "actual-diagnosis" },
            "missing-id" => original with { RequestId = Guid.Empty },
            "negative-revision" => original with { ExpectedRevision = -1 },
            "unknown-action" => original with { Action = (SyntheticUrgentSafetyAction)99 },
            "invalid-ref-format" => original with { UnverifiedConcernRef = "test:bad space" },
            "unexpected-team-at-opening" => original with { ClaimedClinicalTeamRef = "test:team" },
            "unexpected-actor-at-opening" => original with { ClaimedActorRef = "test:author" },
            "unexpected-referral-at-opening" => original with { OriginalReferralRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        var result = Apply(SyntheticUrgentSafetySnapshot.Empty, bad);
        Assert.Equal(SyntheticUrgentSafetyOutcome.Invalid, result.Outcome);
    }

    [Theory]
    [InlineData("warning-no-actor")]
    [InlineData("warning-no-evidence")]
    [InlineData("warning-extra-team")]
    [InlineData("request-no-team")]
    [InlineData("request-no-authority")]
    [InlineData("request-extra-referral-origin")]
    [InlineData("receipt-no-origin-request")]
    [InlineData("receipt-no-actor")]
    [InlineData("receipt-no-evidence")]
    public void IncompleteOrContaminatedPaperNotesAreInvalid(string mutation)
    {
        var root = Root();
        var opened = Apply(SyntheticUrgentSafetySnapshot.Empty, root);
        var w = Warning(root);
        var r = Request(root);
        var c = Receipt(root, r);
        var bad = mutation switch
        {
            "warning-no-actor" => w with { ClaimedActorRef = null },
            "warning-no-evidence" => w with { UnverifiedEvidenceRef = null },
            "warning-extra-team" => w with { ClaimedClinicalTeamRef = "test:team" },
            "request-no-team" => r with { ClaimedClinicalTeamRef = null },
            "request-no-authority" => r with { UnverifiedTeamAuthorityEvidenceRef = null },
            "request-extra-referral-origin" => r with { OriginalReferralRequestId = Guid.NewGuid() },
            "receipt-no-origin-request" => c with { OriginalReferralRequestId = Guid.Empty },
            "receipt-no-actor" => c with { ClaimedActorRef = null },
            "receipt-no-evidence" => c with { UnverifiedEvidenceRef = null },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticUrgentSafetyOutcome.Invalid,
            Apply(opened.Snapshot, bad).Outcome);
    }

    [Fact]
    public void NoDefaultSnapshotOrProjectionIsAnOfficialMedicalCase()
    {
        Assert.Equal(SyntheticUrgentSafetyOutcome.Invalid,
            SyntheticUrgentSafetyTrail.Apply(null, Root()).Outcome);
        Assert.Null(SyntheticUrgentSafetyProjection.Project(null));
        Assert.Null(SyntheticUrgentSafetyProjection.Project(SyntheticUrgentSafetySnapshot.Empty));
        Assert.DoesNotContain(typeof(SyntheticUrgentSafetyRead).GetProperties(),
            prop => new[] { "Diagnosed", "MedicalPriority", "Dispatched",
                "Delivered", "UrgencyScore", "PatientRecord", "ClinicalApproved" }
                .Any(term => prop.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }
}
