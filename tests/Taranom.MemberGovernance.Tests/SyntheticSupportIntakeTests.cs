using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticSupportIntakeTests
{
    private static SyntheticSupportCommand Submit(
        SyntheticSupportTopic topic = SyntheticSupportTopic.MedicalServiceComplaint,
        Guid? request = null, long expected = 0) =>
        new(request ?? Guid.NewGuid(), expected,
            SyntheticSupportAction.SubmitUnverified,
            "test:case-01", "test:member-01", topic,
            "test:opaque-evidence", "test:claimed-member",
            Guid.Empty, null, null);

    private static SyntheticSupportCommand Note(
        SyntheticSupportCommand original, long expected = 1,
        Guid? request = null) => original with
        {
            Action = SyntheticSupportAction.RecordUnverifiedFollowUp,
            RequestId = request ?? Guid.NewGuid(),
            ExpectedRevision = expected,
            OriginalSubmissionRequestId = original.RequestId,
            ClaimedFollowUpActorRef = "test:claimed-support-worker",
            UnverifiedFollowUpNoteRef = "test:opaque-note"
        };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BothIndependentTopicsCreateOnlyUnverifiedIntake(int topicValue)
    {
        var topic = (SyntheticSupportTopic)topicValue;
        var command = Submit(topic);
        var result = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, command);
        Assert.Equal(SyntheticSupportOutcome.RecordedUnverified, result.Outcome);
        Assert.Equal(1, result.Snapshot.Revision);
        var read = SyntheticSupportReadProjection.Project(result.Snapshot);
        Assert.True(read?.OriginalSubmissionPresent);
        Assert.Equal(topic, read?.Topic);
        Assert.Equal(0, read?.UnverifiedNoteCount);
    }

    [Fact]
    public void SeparateFollowUpNotesPreserveIntakeAndOnlyIncrementRevision()
    {
        var original = Submit();
        var first = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, original);
        var second = SyntheticSupportIntake.Apply(first.Snapshot, Note(original));
        var third = SyntheticSupportIntake.Apply(second.Snapshot,
            Note(original, expected: 2));
        Assert.Equal(SyntheticSupportOutcome.RecordedUnverified, third.Outcome);
        Assert.Equal(3, third.Snapshot.Revision);
        Assert.Equal(2, SyntheticSupportReadProjection.Project(third.Snapshot)?.UnverifiedNoteCount);
        Assert.Equal(original, third.Snapshot.History[0].Command);
    }

    [Fact]
    public void ExactHistoricalReplayDoesNotRewindOrDuplicateEvents()
    {
        var original = Submit();
        var first = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, original);
        var followUp = Note(original);
        var latest = SyntheticSupportIntake.Apply(first.Snapshot, followUp);
        var replay = SyntheticSupportIntake.Apply(latest.Snapshot, original);
        var noteReplay = SyntheticSupportIntake.Apply(latest.Snapshot, followUp);
        Assert.Equal(SyntheticSupportOutcome.Replayed, replay.Outcome);
        Assert.Equal(SyntheticSupportOutcome.Replayed, noteReplay.Outcome);
        Assert.Same(latest.Snapshot, replay.Snapshot);
        Assert.Equal(2, replay.Snapshot.Revision);
    }

    [Fact]
    public void ChangedReplayPayloadAlwaysConflicts()
    {
        var original = Submit();
        var first = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, original);
        var changed = original with { Topic = SyntheticSupportTopic.InsuranceCoverageDispute };
        var result = SyntheticSupportIntake.Apply(first.Snapshot, changed);
        Assert.Equal(SyntheticSupportOutcome.ConflictingReplay, result.Outcome);
        Assert.Single(result.Snapshot.History);
    }

    [Fact]
    public void StaleRevisionCannotRecordASecondNote()
    {
        var original = Submit();
        var first = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, original);
        var second = SyntheticSupportIntake.Apply(first.Snapshot, Note(original));
        var stale = SyntheticSupportIntake.Apply(second.Snapshot, Note(original, expected: 1));
        Assert.Equal(SyntheticSupportOutcome.StaleRevision, stale.Outcome);
        Assert.Equal(2, stale.Snapshot.Revision);
    }

    [Fact]
    public void NoInitialSubmissionMeansNoFollowUp()
    {
        var note = Note(Submit(), expected: 0);
        Assert.Equal(SyntheticSupportOutcome.OriginalMissing,
            SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, note).Outcome);
        Assert.Null(SyntheticSupportReadProjection.Project(SyntheticSupportSnapshot.Empty));
    }

    [Theory]
    [InlineData("topic")]
    [InlineData("case")]
    [InlineData("member")]
    [InlineData("evidence")]
    [InlineData("submitter")]
    [InlineData("original-request")]
    public void FollowUpCannotReassignOriginalClaimOrLegalDomain(string mutation)
    {
        var original = Submit();
        var first = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, original);
        var cmd = Note(original);
        cmd = mutation switch
        {
            "topic" => cmd with { Topic = SyntheticSupportTopic.InsuranceCoverageDispute },
            "case" => cmd with { CaseRef = "test:case-other" },
            "member" => cmd with { MemberRef = "test:member-other" },
            "evidence" => cmd with { UnverifiedEvidenceRef = "test:other-evidence" },
            "submitter" => cmd with { ClaimedSubmitterRef = "test:other-actor" },
            "original-request" => cmd with { OriginalSubmissionRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException()
        };
        var result = SyntheticSupportIntake.Apply(first.Snapshot, cmd);
        Assert.Equal(SyntheticSupportOutcome.ScopeMismatch, result.Outcome);
        Assert.Single(result.Snapshot.History);
    }

    [Theory]
    [InlineData("real-case")]
    [InlineData("real-member")]
    [InlineData("wrong-topic")]
    [InlineData("wrong-action")]
    [InlineData("empty-request")]
    [InlineData("negative-revision")]
    [InlineData("extra-note-on-submission")]
    [InlineData("missing-note-ref")]
    [InlineData("missing-followup-actor")]
    public void InvalidOrRealAuthorityInputNeverGetsRecorded(string mutation)
    {
        var cmd = Submit();
        cmd = mutation switch
        {
            "real-case" => cmd with { CaseRef = "real:case" },
            "real-member" => cmd with { MemberRef = "real:member" },
            "wrong-topic" => cmd with { Topic = (SyntheticSupportTopic)99 },
            "wrong-action" => cmd with { Action = (SyntheticSupportAction)99 },
            "empty-request" => cmd with { RequestId = Guid.Empty },
            "negative-revision" => cmd with { ExpectedRevision = -1 },
            "extra-note-on-submission" => cmd with { UnverifiedFollowUpNoteRef = "test:injected" },
            "missing-note-ref" => Note(cmd, expected: 0) with { UnverifiedFollowUpNoteRef = null },
            "missing-followup-actor" => Note(cmd, expected: 0) with { ClaimedFollowUpActorRef = null },
            _ => throw new ArgumentException()
        };
        var result = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, cmd);
        Assert.Equal(SyntheticSupportOutcome.Invalid, result.Outcome);
        Assert.Empty(result.Snapshot.History);
    }

    [Fact]
    public void DuplicateNewIntakeForExistingCaseCannotOverwriteOriginal()
    {
        var original = Submit();
        var first = SyntheticSupportIntake.Apply(SyntheticSupportSnapshot.Empty, original);
        var second = SyntheticSupportIntake.Apply(first.Snapshot, Submit(expected: 1));
        Assert.Equal(SyntheticSupportOutcome.ScopeMismatch, second.Outcome);
    }

    [Fact]
    public void NoAutomaticMedicalVerdictOrInsurerDecisionVocabularyExists()
    {
        var forbidden = new[] { "Approved", "Resolved", "Paid", "Reimbursed",
            "Covered", "Liability", "Diagnosis", "InsurerAccepted", "Wallet" };
        foreach (var field in Enum.GetNames<SyntheticSupportOutcome>()
            .Concat(Enum.GetNames<SyntheticSupportAction>())
            .Concat(Enum.GetNames<SyntheticSupportTopic>()))
            Assert.DoesNotContain(forbidden, word =>
                field.Contains(word, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(SyntheticSupportCommand).GetProperties(),
            property => property.Name.Contains("MedicalRecord") ||
                        property.Name.Contains("Amount") ||
                        property.Name.Contains("Payment") ||
                        property.Name.Contains("InsuranceNumber"));
    }
}
