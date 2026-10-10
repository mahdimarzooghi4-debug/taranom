using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticAiReleaseEvidenceTrailTests
{
    private const string Candidate = "test:ai-candidate-a";
    private const string Dataset = "test:consent-controlled-dataset-v1";
    private static readonly string Digest = new('A', 64);

    private static SyntheticAiReleaseEvidenceCommand CreateCandidate(
        Guid? request = null, long revision = 0) =>
        new(request ?? Guid.NewGuid(), revision,
            SyntheticAiReleaseEvidenceAction.RecordUnverifiedCandidate,
            Candidate, Dataset, Digest, Guid.Empty, null, null, null, null, null);

    private static SyntheticAiReleaseEvidenceCommand Committee(
        SyntheticAiReleaseEvidenceCommand origin, long revision = 1,
        Guid? request = null) =>
        origin with
        {
            RequestId = request ?? Guid.NewGuid(),
            ExpectedRevision = revision,
            Action = SyntheticAiReleaseEvidenceAction.RecordUnverifiedJointCommitteePacket,
            OriginalCandidateRequestId = origin.RequestId,
            ClaimedMedicalCommitteeActorRef = "test:claimed-doctor",
            ClaimedTechnicalCommitteeActorRef = "test:claimed-engineer",
            UnverifiedJointCommitteePacketRef = "test:unverified-joint-minutes"
        };

    private static SyntheticAiReleaseEvidenceCommand Final(
        SyntheticAiReleaseEvidenceCommand origin, long revision = 2,
        Guid? request = null) =>
        origin with
        {
            RequestId = request ?? Guid.NewGuid(),
            ExpectedRevision = revision,
            Action = SyntheticAiReleaseEvidenceAction.RecordUnverifiedFinalHumanPacket,
            OriginalCandidateRequestId = origin.RequestId,
            ClaimedFinalAuthorityActorRef = "test:claimed-independent-authority",
            UnverifiedFinalHumanPacketRef = "test:unverified-final-human-note"
        };

    [Fact]
    public void AClaimedCandidateIsNotAnApprovedModel()
    {
        var origin = CreateCandidate();
        var r = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.RecordedUnverified, r.Outcome);
        Assert.Equal(1, r.Snapshot.Revision);
        var projection = SyntheticAiReleaseEvidenceProjection.Project(r.Snapshot)!.Value;
        Assert.True(projection.UnverifiedCandidateRecorded);
        Assert.False(projection.UnverifiedCommitteePacketRecorded);
        Assert.False(projection.UnverifiedFinalHumanPacketRecorded);
    }

    [Fact]
    public void JointMedicalTechnicalPacketAndSeparateHumanPacketAreDistinctSteps()
    {
        var origin = CreateCandidate();
        var first = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        var second = SyntheticAiReleaseEvidenceTrail.Apply(first.Snapshot, Committee(origin));
        var third = SyntheticAiReleaseEvidenceTrail.Apply(second.Snapshot, Final(origin));
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.RecordedUnverified, second.Outcome);
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.RecordedUnverified, third.Outcome);
        Assert.Equal(3, third.Snapshot.Revision);
        var view = SyntheticAiReleaseEvidenceProjection.Project(third.Snapshot)!.Value;
        Assert.True(view.UnverifiedCommitteePacketRecorded);
        Assert.True(view.UnverifiedFinalHumanPacketRecorded);
        Assert.Equal(Candidate, view.CandidateRef);
    }

    [Fact]
    public void AClaimedFinalHumanPacketWithoutCommitteeEvidenceFails()
    {
        var origin = CreateCandidate();
        var first = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        var result = SyntheticAiReleaseEvidenceTrail.Apply(first.Snapshot, Final(origin, revision: 1));
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.MissingCommitteePacket, result.Outcome);
        Assert.Equal(1, result.Snapshot.Revision);
    }

    [Fact]
    public void NoCandidateMeansNeitherCommitteeNorFinalHumanPacket()
    {
        var origin = CreateCandidate();
        foreach (var packet in new[] { Committee(origin, revision: 0), Final(origin, revision: 0) })
        {
            var result = SyntheticAiReleaseEvidenceTrail.Apply(
                SyntheticAiReleaseEvidenceSnapshot.Empty, packet);
            Assert.Equal(SyntheticAiReleaseEvidenceOutcome.MissingCandidate, result.Outcome);
            Assert.Empty(result.Snapshot.Events);
        }
    }

    [Fact]
    public void RepeatedCommitteeOrFinalStagesCannotFabricateAnotherPromotion()
    {
        var origin = CreateCandidate();
        var first = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        var second = SyntheticAiReleaseEvidenceTrail.Apply(first.Snapshot, Committee(origin));
        var duplicate = SyntheticAiReleaseEvidenceTrail.Apply(second.Snapshot,
            Committee(origin, revision: 2));
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.DuplicateStage, duplicate.Outcome);
        var third = SyntheticAiReleaseEvidenceTrail.Apply(second.Snapshot, Final(origin));
        var last = SyntheticAiReleaseEvidenceTrail.Apply(third.Snapshot,
            Final(origin, revision: 3));
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.DuplicateStage, last.Outcome);
    }

    [Fact]
    public void CandidateCannotBeReregisteredAsAnotherModel()
    {
        var origin = CreateCandidate();
        var first = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.DuplicateStage,
            SyntheticAiReleaseEvidenceTrail.Apply(first.Snapshot,
                CreateCandidate(revision: 1)).Outcome);
    }

    [Fact]
    public void ExactHistoricalReplaysKeepLatestRevision()
    {
        var origin = CreateCandidate();
        var one = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        var committee = Committee(origin);
        var two = SyntheticAiReleaseEvidenceTrail.Apply(one.Snapshot, committee);
        var final = Final(origin);
        var three = SyntheticAiReleaseEvidenceTrail.Apply(two.Snapshot, final);
        foreach (var command in new[] { origin, committee, final })
        {
            var replay = SyntheticAiReleaseEvidenceTrail.Apply(three.Snapshot, command);
            Assert.Equal(SyntheticAiReleaseEvidenceOutcome.Replayed, replay.Outcome);
            Assert.Same(three.Snapshot, replay.Snapshot);
        }
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("dataset")]
    [InlineData("digest")]
    [InlineData("origin")]
    public void CannotRebindCommitteeToAnotherDatasetOrCandidate(string change)
    {
        var origin = CreateCandidate();
        var one = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        var command = Committee(origin);
        command = change switch
        {
            "candidate" => command with { CandidateRef = "test:another-candidate" },
            "dataset" => command with { DatasetVersionRef = "test:other-dataset" },
            "digest" => command with { ArtifactDigestSha256 = new string('B', 64) },
            "origin" => command with { OriginalCandidateRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        var result = SyntheticAiReleaseEvidenceTrail.Apply(one.Snapshot, command);
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.ScopeMismatch, result.Outcome);
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("dataset")]
    [InlineData("digest")]
    [InlineData("origin")]
    public void CannotRebindFinalHumanPacketToAnotherModel(string change)
    {
        var origin = CreateCandidate();
        var one = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        var two = SyntheticAiReleaseEvidenceTrail.Apply(one.Snapshot, Committee(origin));
        var command = Final(origin);
        command = change switch
        {
            "candidate" => command with { CandidateRef = "test:another-candidate" },
            "dataset" => command with { DatasetVersionRef = "test:other-dataset" },
            "digest" => command with { ArtifactDigestSha256 = new string('B', 64) },
            "origin" => command with { OriginalCandidateRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.ScopeMismatch,
            SyntheticAiReleaseEvidenceTrail.Apply(two.Snapshot, command).Outcome);
    }

    [Fact]
    public void SameClaimedMedicalAndTechnicalPersonCannotStandInForBothRoles()
    {
        var origin = CreateCandidate();
        var one = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        var packet = Committee(origin) with
        {
            ClaimedTechnicalCommitteeActorRef = "test:claimed-doctor"
        };
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.ClaimedActorConflict,
            SyntheticAiReleaseEvidenceTrail.Apply(one.Snapshot, packet).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameClaimedFinalPersonCannotReplaceIndependentCommitteeRoles(bool medical)
    {
        var origin = CreateCandidate();
        var one = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        var two = SyntheticAiReleaseEvidenceTrail.Apply(one.Snapshot, Committee(origin));
        var final = Final(origin) with
        {
            ClaimedFinalAuthorityActorRef = medical
                ? "test:claimed-doctor" : "test:claimed-engineer"
        };
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.ClaimedActorConflict,
            SyntheticAiReleaseEvidenceTrail.Apply(two.Snapshot, final).Outcome);
    }

    [Fact]
    public void ChangedPayloadUnderSameRequestIdAlwaysConflicts()
    {
        var origin = CreateCandidate();
        var one = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.ConflictingReplay,
            SyntheticAiReleaseEvidenceTrail.Apply(one.Snapshot,
                origin with { DatasetVersionRef = "test:swapped-data" }).Outcome);
        Assert.Same(one.Snapshot, SyntheticAiReleaseEvidenceTrail.Apply(one.Snapshot,
            origin with { DatasetVersionRef = "test:swapped-data" }).Snapshot);
    }

    [Fact]
    public void StaleExpectedRevisionDoesNotRecordHumanPacket()
    {
        var origin = CreateCandidate();
        var one = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, origin);
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.StaleRevision,
            SyntheticAiReleaseEvidenceTrail.Apply(one.Snapshot,
                Committee(origin, revision: 0)).Outcome);
    }

    [Theory]
    [InlineData("real-candidate")]
    [InlineData("real-dataset")]
    [InlineData("bad-hash")]
    [InlineData("missing-request")]
    [InlineData("negative-revision")]
    [InlineData("unknown-action")]
    [InlineData("illegal-committee-at-candidate")]
    [InlineData("illegal-owner-at-candidate")]
    [InlineData("extra-owner-at-committee")]
    [InlineData("extra-committee-at-final")]
    [InlineData("missing-committee-minute")]
    [InlineData("missing-medical-role")]
    [InlineData("missing-technical-role")]
    [InlineData("missing-final-authority")]
    [InlineData("missing-final-note")]
    [InlineData("missing-origin")]
    public void InvalidClaimedEvidenceNeverCreatesAStage(string mutation)
    {
        var origin = CreateCandidate();
        var command = mutation switch
        {
            "real-candidate" => origin with { CandidateRef = "real:uncertified" },
            "real-dataset" => origin with { DatasetVersionRef = "production-health-data" },
            "bad-hash" => origin with { ArtifactDigestSha256 = "unverified-checksum" },
            "missing-request" => origin with { RequestId = Guid.Empty },
            "negative-revision" => origin with { ExpectedRevision = -1 },
            "unknown-action" => origin with { Action = (SyntheticAiReleaseEvidenceAction)999 },
            "illegal-committee-at-candidate" => origin with { ClaimedMedicalCommitteeActorRef = "test:actor" },
            "illegal-owner-at-candidate" => origin with { ClaimedFinalAuthorityActorRef = "test:actor" },
            "extra-owner-at-committee" => Committee(origin, 0) with { ClaimedFinalAuthorityActorRef = "test:authority" },
            "extra-committee-at-final" => Final(origin, 0) with { ClaimedTechnicalCommitteeActorRef = "test:engineer" },
            "missing-committee-minute" => Committee(origin, 0) with { UnverifiedJointCommitteePacketRef = null },
            "missing-medical-role" => Committee(origin, 0) with { ClaimedMedicalCommitteeActorRef = null },
            "missing-technical-role" => Committee(origin, 0) with { ClaimedTechnicalCommitteeActorRef = null },
            "missing-final-authority" => Final(origin, 0) with { ClaimedFinalAuthorityActorRef = null },
            "missing-final-note" => Final(origin, 0) with { UnverifiedFinalHumanPacketRef = null },
            "missing-origin" => Final(origin, 0) with { OriginalCandidateRequestId = Guid.Empty },
            _ => throw new ArgumentException()
        };
        var result = SyntheticAiReleaseEvidenceTrail.Apply(
            SyntheticAiReleaseEvidenceSnapshot.Empty, command);
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.Invalid, result.Outcome);
        Assert.Empty(result.Snapshot.Events);
    }

    [Fact]
    public void NullHistoryNeverImplicitlyCreatesProductionCandidate()
    {
        Assert.Equal(SyntheticAiReleaseEvidenceOutcome.Invalid,
            SyntheticAiReleaseEvidenceTrail.Apply(null, CreateCandidate()).Outcome);
        Assert.Null(SyntheticAiReleaseEvidenceProjection.Project(null));
    }

    [Fact]
    public void NoAutomaticModelPromotionOrTrainingProductionAPIExistsInContract()
    {
        var forbidden = new[] { "ProductionActive", "Deploy", "Train", "Promoted",
            "Activated", "ModelReady", "Authorize", "Approved", "Infer" };
        foreach (var name in Enum.GetNames<SyntheticAiReleaseEvidenceAction>()
            .Concat(Enum.GetNames<SyntheticAiReleaseEvidenceOutcome>()))
            Assert.DoesNotContain(forbidden, term =>
                name.Contains(term, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(SyntheticAiReleaseEvidenceCommand).GetProperties(),
            p => p.Name.Contains("Endpoint") || p.Name.Contains("Token") ||
                 p.Name.Contains("MedicalRecord") || p.Name.Contains("TrainingRate"));
    }
}
