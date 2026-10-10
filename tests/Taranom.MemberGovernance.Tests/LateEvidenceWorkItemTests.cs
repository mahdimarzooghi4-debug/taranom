using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class LateEvidenceWorkItemTests
{
    private static readonly DateTimeOffset From =
        new(2026, 1, 12, 11, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset To = From.AddDays(9);

    private static LateEvidenceCommand Submit(Guid? request = null,
        long revision = 0) => new(
        request ?? Guid.NewGuid(), revision,
        LateEvidenceAction.SubmitUnverifiedEvidence, "test:case-alpha",
        "test:member-alpha", From, To, "test:opaque-document-reference",
        "test:claimed-submitter", Guid.Empty, null, null);

    private static LateEvidenceCommand Note(LateEvidenceCommand origin,
        Guid? request = null, long revision = 1) =>
        origin with
        {
            Action = LateEvidenceAction.RecordUnverifiedHumanNote,
            RequestId = request ?? Guid.NewGuid(),
            ExpectedRevision = revision,
            OriginalSubmissionRequestId = origin.RequestId,
            ReviewerClaimRef = "test:claimed-reviewer",
            ReviewNoteEvidenceRef = "test:human-note-reference"
        };

    [Fact]
    public void SubmissionOnlyCreatesUnverifiedWorkItem()
    {
        var submitted = Submit();
        var result = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, submitted);
        Assert.Equal(LateEvidenceOutcome.RecordedUnverified, result.Outcome);
        Assert.Equal(1, result.Snapshot.Revision);
        Assert.Single(result.Snapshot.Events);
        Assert.Equal(LateEvidenceAction.SubmitUnverifiedEvidence,
            result.Snapshot.Events[0].Command.Action);
    }

    [Fact]
    public void HumanNoteHasNoApprovalAndMustBindOriginalSubmission()
    {
        var submitted = Submit();
        var first = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, submitted);
        var note = Note(submitted);
        var result = LateEvidenceWorkItem.Apply(first.Snapshot, note);
        Assert.Equal(LateEvidenceOutcome.RecordedUnverified, result.Outcome);
        Assert.Equal(2, result.Snapshot.Revision);
        Assert.Equal(submitted.RequestId,
            result.Snapshot.Events[1].Command.OriginalSubmissionRequestId);
        Assert.DoesNotContain(Enum.GetNames<LateEvidenceOutcome>(),
            value => value.Contains("Approved") || value.Contains("Entitled") ||
                     value.Contains("Credited") || value.Contains("Paid"));
    }

    [Fact]
    public void MultipleNotesAppendWithoutAnyFinalApprovalState()
    {
        var original = Submit();
        var one = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, original);
        var firstNote = Note(original);
        var two = LateEvidenceWorkItem.Apply(one.Snapshot, firstNote);
        var three = LateEvidenceWorkItem.Apply(two.Snapshot,
            Note(original, revision: 2));
        Assert.Equal(LateEvidenceOutcome.RecordedUnverified, three.Outcome);
        Assert.Equal(3, three.Snapshot.Events.Length);
    }

    [Fact]
    public void HistoricalReplayAfterOtherNotesDoesNotRewind()
    {
        var original = Submit();
        var first = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, original);
        var note = Note(original);
        var second = LateEvidenceWorkItem.Apply(first.Snapshot, note);
        Assert.Equal(LateEvidenceOutcome.Replayed,
            LateEvidenceWorkItem.Apply(second.Snapshot, original).Outcome);
        Assert.Equal(LateEvidenceOutcome.Replayed,
            LateEvidenceWorkItem.Apply(second.Snapshot, note).Outcome);
        Assert.Equal(2, second.Snapshot.Revision);
    }

    [Fact]
    public void ReusedKeyWithDifferentEvidenceIsAlwaysConflict()
    {
        var original = Submit();
        var first = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, original);
        var changed = original with { EvidenceRef = "test:altered" };
        Assert.Equal(LateEvidenceOutcome.ConflictingReplay,
            LateEvidenceWorkItem.Apply(first.Snapshot, changed).Outcome);
        Assert.Same(first.Snapshot,
            LateEvidenceWorkItem.Apply(first.Snapshot, changed).Snapshot);
    }

    [Fact]
    public void ConcurrentStaleRevisionCannotAppend()
    {
        var original = Submit();
        var first = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, original);
        var stale = Note(original, revision: 0);
        Assert.Equal(LateEvidenceOutcome.StaleRevision,
            LateEvidenceWorkItem.Apply(first.Snapshot, stale).Outcome);
        Assert.Single(first.Snapshot.Events);
    }

    [Fact]
    public void NoteWithoutSubmissionFailsClosed()
    {
        var note = Note(Submit(), revision: 0);
        Assert.Equal(LateEvidenceOutcome.SubmissionMissing,
            LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, note).Outcome);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("case")]
    [InlineData("period-start")]
    [InlineData("period-end")]
    [InlineData("evidence")]
    [InlineData("submitter")]
    [InlineData("original-request")]
    public void NoteCannotChangeOriginalClaimOrLineage(string tamper)
    {
        var original = Submit();
        var first = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, original);
        var note = Note(original);
        note = tamper switch
        {
            "member" => note with { MemberRef = "test:other-member" },
            "case" => note with { CaseRef = "test:other-case" },
            "period-start" => note with { PeriodStartInclusive = From.AddTicks(1) },
            "period-end" => note with { PeriodEndExclusive = To.AddTicks(-1) },
            "evidence" => note with { EvidenceRef = "test:other-document" },
            "submitter" => note with { SubmittedByClaimRef = "test:other-actor" },
            "original-request" => note with { OriginalSubmissionRequestId = Guid.NewGuid() },
            _ => throw new ArgumentException("Unexpected mutation.")
        };
        Assert.Equal(LateEvidenceOutcome.ScopeMismatch,
            LateEvidenceWorkItem.Apply(first.Snapshot, note).Outcome);
    }

    [Fact]
    public void MatchingClaimedActorIsRejectedWithoutPretendingRealIndependence()
    {
        var original = Submit();
        var first = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, original);
        Assert.Equal(LateEvidenceOutcome.SameClaimedActor,
            LateEvidenceWorkItem.Apply(first.Snapshot,
                Note(original) with { ReviewerClaimRef = original.SubmittedByClaimRef }).Outcome);
    }

    [Theory]
    [InlineData("real-subject")]
    [InlineData("empty-request")]
    [InlineData("negative-revision")]
    [InlineData("wrong-action")]
    [InlineData("invalid-range")]
    [InlineData("real-document")]
    [InlineData("unexpected-review-on-submission")]
    [InlineData("missing-note")]
    [InlineData("missing-original")]
    public void InvalidOrNonSyntheticCommandsNeverEnterHistory(string variant)
    {
        var original = Submit();
        var cmd = variant switch
        {
            "real-subject" => original with { MemberRef = "real:member" },
            "empty-request" => original with { RequestId = Guid.Empty },
            "negative-revision" => original with { ExpectedRevision = -1 },
            "wrong-action" => original with { Action = (LateEvidenceAction)99 },
            "invalid-range" => original with { PeriodEndExclusive = original.PeriodStartInclusive },
            "real-document" => original with { EvidenceRef = "real:document" },
            "unexpected-review-on-submission" => original with { ReviewerClaimRef = "test:reviewer" },
            "missing-note" => Note(original, revision: 0) with { ReviewNoteEvidenceRef = null },
            "missing-original" => Note(original, revision: 0) with { OriginalSubmissionRequestId = Guid.Empty },
            _ => throw new ArgumentException("Unknown case.")
        };
        var result = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, cmd);
        Assert.Equal(LateEvidenceOutcome.Invalid, result.Outcome);
        Assert.Empty(result.Snapshot.Events);
    }

    [Fact]
    public void DuplicateSubmissionForOneCaseIsNeverAnotherBenefit()
    {
        var original = Submit();
        var first = LateEvidenceWorkItem.Apply(LateEvidenceCaseSnapshot.Empty, original);
        var second = Submit(Guid.NewGuid(), 1);
        Assert.Equal(LateEvidenceOutcome.ScopeMismatch,
            LateEvidenceWorkItem.Apply(first.Snapshot, second).Outcome);
    }

    [Fact]
    public void NoFinancialOrRealIdentityDataInTheContract()
    {
        var forbidden = new[] { "Amount", "Balance", "Wallet", "Credit", "Approved",
            "Price", "NationalId", "Bank", "Paid", "Payment", "Income" };
        foreach (var prop in typeof(LateEvidenceCommand).GetProperties())
            Assert.DoesNotContain(forbidden, word =>
                prop.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
