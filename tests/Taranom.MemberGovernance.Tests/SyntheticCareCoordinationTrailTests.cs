using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticCareCoordinationTrailTests
{
    private static SyntheticCareTrailCommand Start(
        Guid? request = null, long expected = 0) =>
        new(request ?? Guid.NewGuid(), expected,
            SyntheticCareTrailAction.RequestNonclinicalCoordination,
            "test:case-one", "test:member-one", "test:request-reason-ref",
            Guid.Empty, null, null);

    private static SyntheticCareTrailCommand FollowUp(
        SyntheticCareTrailCommand original, int action = 2,
        long expected = 1, Guid? request = null) =>
        original with
        {
            RequestId = request ?? Guid.NewGuid(),
            ExpectedRevision = expected,
            Action = (SyntheticCareTrailAction)action,
            OriginalSubmissionRequestId = original.RequestId,
            ClaimedActorRef = action == 2
                ? "test:claimed-coordinator" : "test:claimed-clinical-source",
            UnverifiedNoteOrDocumentRef = action == 2
                ? "test:coordination-note-ref" : "test:opaque-clinical-document-ref"
        };

    [Fact]
    public void NewRequestOnlyRecordsUnverifiedNonclinicalCoordination()
    {
        var input = Start();
        var response = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty, input);
        Assert.Equal(SyntheticCareTrailOutcome.RecordedUnverified, response.Outcome);
        Assert.Equal(1, response.Snapshot.Revision);
        Assert.Equal(input, response.Snapshot.History[0].Command);
        var projection = SyntheticCareTrailProjection.Project(response.Snapshot);
        Assert.NotNull(projection);
        Assert.Equal(0, projection.Value.UnverifiedCoordinatorContactCount);
        Assert.Equal(0, projection.Value.UnverifiedClinicalReferenceCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void TwoDistinctRecordedClaimsCannotGrantClinicalAuthority(int action)
    {
        var origin = Start();
        var first = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty, origin);
        var second = SyntheticCareCoordinationTrail.Apply(first.Snapshot, FollowUp(origin, action));
        Assert.Equal(SyntheticCareTrailOutcome.RecordedUnverified, second.Outcome);
        var read = SyntheticCareTrailProjection.Project(second.Snapshot)!.Value;
        Assert.Equal(action == 2 ? 1 : 0, read.UnverifiedCoordinatorContactCount);
        Assert.Equal(action == 3 ? 1 : 0, read.UnverifiedClinicalReferenceCount);
        Assert.Equal(2, read.Revision);
    }

    [Fact]
    public void BothClaimLanesRemainSeparatedAndNeverBecomeCompletedReferral()
    {
        var original = Start();
        var one = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty, original);
        var two = SyntheticCareCoordinationTrail.Apply(one.Snapshot, FollowUp(original));
        var three = SyntheticCareCoordinationTrail.Apply(two.Snapshot, FollowUp(original, 3, 2));
        var projection = SyntheticCareTrailProjection.Project(three.Snapshot)!.Value;
        Assert.Equal(3, projection.Revision);
        Assert.Equal(1, projection.UnverifiedCoordinatorContactCount);
        Assert.Equal(1, projection.UnverifiedClinicalReferenceCount);
        Assert.All(three.Snapshot.History, e => Assert.StartsWith("test:", e.Command.MemberRef));
    }

    [Fact]
    public void OldSubmissionAndNoteReplaysDoNotRewind()
    {
        var original = Start();
        var one = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty, original);
        var note = FollowUp(original);
        var two = SyntheticCareCoordinationTrail.Apply(one.Snapshot, note);
        var three = SyntheticCareCoordinationTrail.Apply(two.Snapshot, FollowUp(original, 3, 2));
        Assert.Equal(SyntheticCareTrailOutcome.Replayed,
            SyntheticCareCoordinationTrail.Apply(three.Snapshot, original).Outcome);
        var replay = SyntheticCareCoordinationTrail.Apply(three.Snapshot, note);
        Assert.Equal(SyntheticCareTrailOutcome.Replayed, replay.Outcome);
        Assert.Same(three.Snapshot, replay.Snapshot);
    }

    [Fact]
    public void SameRequestWithChangedActorOrActionAlwaysConflicts()
    {
        var original = Start();
        var first = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty, original);
        var follow = FollowUp(original);
        var second = SyntheticCareCoordinationTrail.Apply(first.Snapshot, follow);
        var forged = follow with { Action = SyntheticCareTrailAction.AttachUnverifiedClinicalSourceReference };
        Assert.Equal(SyntheticCareTrailOutcome.ConflictingReplay,
            SyntheticCareCoordinationTrail.Apply(second.Snapshot, forged).Outcome);
        Assert.Equal(SyntheticCareTrailOutcome.ConflictingReplay,
            SyntheticCareCoordinationTrail.Apply(second.Snapshot,
                follow with { ClaimedActorRef = "test:someone-else" }).Outcome);
        Assert.Equal(2, second.Snapshot.Revision);
    }

    [Fact]
    public void StaleRevisionAndDuplicateNewRequestAreDenied()
    {
        var original = Start();
        var first = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty, original);
        Assert.Equal(SyntheticCareTrailOutcome.StaleRevision,
            SyntheticCareCoordinationTrail.Apply(first.Snapshot, FollowUp(original, expected: 0)).Outcome);
        Assert.Equal(SyntheticCareTrailOutcome.ScopeMismatch,
            SyntheticCareCoordinationTrail.Apply(first.Snapshot, Start(expected: 1)).Outcome);
    }

    [Fact]
    public void NoOriginalMeansNoCoordinationNoteOrClinicalReference()
    {
        foreach (var action in new[] { 2, 3 })
        {
            var result = SyntheticCareCoordinationTrail.Apply(
                SyntheticCareTrailSnapshot.Empty, FollowUp(Start(), action, expected: 0));
            Assert.Equal(SyntheticCareTrailOutcome.OriginalMissing, result.Outcome);
            Assert.Empty(result.Snapshot.History);
        }
        Assert.Null(SyntheticCareTrailProjection.Project(SyntheticCareTrailSnapshot.Empty));
    }

    [Theory]
    [InlineData("case")]
    [InlineData("member")]
    [InlineData("request-ref")]
    [InlineData("original-request")]
    public void LaterEventCannotChangeOriginalRequestScope(string mutation)
    {
        var original = Start();
        var one = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty, original);
        var note = FollowUp(original);
        note = mutation switch
        {
            "case" => note with { CaseRef = "test:case-other" },
            "member" => note with { MemberRef = "test:member-other" },
            "request-ref" => note with { OriginalMemberRequestRef = "test:changed-reason" },
            "original-request" => note with { OriginalSubmissionRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException("Unknown mutation.")
        };
        var result = SyntheticCareCoordinationTrail.Apply(one.Snapshot, note);
        Assert.Equal(SyntheticCareTrailOutcome.ScopeMismatch, result.Outcome);
        Assert.Single(result.Snapshot.History);
    }

    [Theory]
    [InlineData("real-member")]
    [InlineData("real-case")]
    [InlineData("unverified-reason")]
    [InlineData("missing-id")]
    [InlineData("negative-revision")]
    [InlineData("unexpected-source-on-request")]
    [InlineData("forged-clinical-action")]
    [InlineData("missing-note")]
    [InlineData("missing-actor")]
    [InlineData("missing-original")]
    public void InvalidOrPretendRealDataDoesNotEnterHistory(string mutation)
    {
        var original = Start();
        var input = mutation switch
        {
            "real-member" => original with { MemberRef = "real:member" },
            "real-case" => original with { CaseRef = "real:case" },
            "unverified-reason" => original with { OriginalMemberRequestRef = "actual-health-data" },
            "missing-id" => original with { RequestId = Guid.Empty },
            "negative-revision" => original with { ExpectedRevision = -1 },
            "unexpected-source-on-request" => original with { ClaimedActorRef = "test:doctor" },
            "forged-clinical-action" => original with { Action = (SyntheticCareTrailAction)99 },
            "missing-note" => FollowUp(original, 3, 0) with { UnverifiedNoteOrDocumentRef = null },
            "missing-actor" => FollowUp(original, 2, 0) with { ClaimedActorRef = null },
            "missing-original" => FollowUp(original, 2, 0) with { OriginalSubmissionRequestId = Guid.Empty },
            _ => throw new ArgumentException()
        };
        var result = SyntheticCareCoordinationTrail.Apply(SyntheticCareTrailSnapshot.Empty, input);
        Assert.Equal(SyntheticCareTrailOutcome.Invalid, result.Outcome);
        Assert.Empty(result.Snapshot.History);
    }

    [Fact]
    public void DomainCannotExpressDiagnosisReferralApprovalOrAppointment()
    {
        string[] forbidden = ["Approved", "Diagnosed", "Prescription", "Scheduled",
            "ReferralIssued", "TreatmentStarted", "CarePlanActive", "Paid", "Completed"];
        foreach (var value in Enum.GetNames<SyntheticCareTrailAction>()
            .Concat(Enum.GetNames<SyntheticCareTrailOutcome>()))
            Assert.DoesNotContain(forbidden, f => value.Contains(f, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(SyntheticCareTrailCommand).GetProperties(),
            p => p.Name.Contains("PatientRecord") || p.Name.Contains("Diagnosis") ||
                 p.Name.Contains("Treatment") || p.Name.Contains("Amount") ||
                 p.Name.Contains("Slot") || p.Name.Contains("ProviderId"));
    }

    [Fact]
    public void NullSnapshotCannotBeImplicitlyCreatedByCaller()
    {
        var result = SyntheticCareCoordinationTrail.Apply(null, Start());
        Assert.Equal(SyntheticCareTrailOutcome.Invalid, result.Outcome);
        Assert.Null(SyntheticCareTrailProjection.Project(null));
    }
}
