using Taranom.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Never authorize human review. No disclosure means UNKNOWN, not clearance.
// An externally unverified review provenance cannot access disclosure data.
internal enum ReviewerConflictAdmission
{
    ReviewerProvenanceUnverified = 1,
    RelationshipStatusUnknown = 2,
    PotentialRelationshipNeedsIndependentReview = 3,
    DisclosureHistoryCorrupt = 4
}

internal sealed class PgReviewerConflictAdmission(
    PgReviewerProvenanceInterlock provenance,
    PgReviewerConflictDisclosureJournal notices)
{
    internal async Task<ReviewerConflictAdmission> InspectAsync(
        ReviewerProvenanceRequest request, CancellationToken ct = default)
    {
        var provenanceResult = await provenance.InspectAsync(request, ct);
        if (provenanceResult != ReviewerProvenanceOutcome.PreconditionsPresentOnly)
            return ReviewerConflictAdmission.ReviewerProvenanceUnverified;

        try
        {
            var result = await notices.ReadAsync(request.CenterRef,
                request.SubjectRef, request.SubjectKind, request.EvidenceRevision,
                request.ReviewNoteRequestId, ct);
            return result.ScreeningStatus ==
                ConflictScreeningStatus.PotentialRelationshipRequiresIndependentReview
                ? ReviewerConflictAdmission.PotentialRelationshipNeedsIndependentReview
                : ReviewerConflictAdmission.RelationshipStatusUnknown;
        }
        catch (InvalidOperationException)
        {
            return ReviewerConflictAdmission.DisclosureHistoryCorrupt;
        }
        catch (ArgumentException)
        {
            return ReviewerConflictAdmission.ReviewerProvenanceUnverified;
        }
    }
}
