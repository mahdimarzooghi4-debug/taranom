namespace Taranom.MemberGovernance;

// TRN-BIZ-047: member-directed prevention, reviewed education and continuity
// of clinical care are distinct responsibilities. This is INTERNAL/TEST-ONLY.
// It does not create consent, publish content or start a medical care plan.
internal enum SyntheticPreventionTrack
{
    HealthEducation = 1,
    PreventiveCoordination = 2,
    LongitudinalClinicalCare = 3
}

internal enum SyntheticParticipationOrigin
{
    MemberRequested = 1,
    QualifiedClinicianProposed = 2
}

internal enum SyntheticPreventionReviewPurpose
{
    InspectParticipationPrerequisites = 1,
    InspectEducationalContentPrerequisites = 2,
    StartOrChangeClinicalPlan = 3,
    ReuseMemberDataForResearchOrAi = 4
}

internal enum SyntheticPreventionReviewResult
{
    InvalidScope = 1,
    ProposerScopeDenied = 2,
    DistinctConsentRequired = 3,
    ProhibitedAutomaticAction = 4,
    MemberDeclined = 5,
    EvidenceRejected = 6,
    EvidenceMissing = 7,
    PreconditionsPresentOnly = 8
}

internal readonly record struct SyntheticPreventionEvidence(
    SyntheticPreventionTrack Track,
    SyntheticParticipationOrigin Origin,
    SyntheticPreventionReviewPurpose Purpose,
    string? TenantRef,
    string? MemberRef,
    string? MemberParticipationSubjectRef,
    string? ProposedProgramRef,
    string? ClaimedProposerRef,
    string? ClaimedResponsibleClinicianRef,
    EvidenceCheck IndependentMemberParticipationChoice,
    EvidenceCheck CurrentMemberIdentityLink,
    EvidenceCheck PurposeSpecificDataBoundary,
    EvidenceCheck LawfulParticipationBasis,
    EvidenceCheck ProfessionalQualificationCurrent,
    EvidenceCheck ClinicalResponsibilityCurrent,
    EvidenceCheck ProposedCarePlanHumanReview,
    EvidenceCheck EducationScientificReview,
    EvidenceCheck EducationContentRights,
    EvidenceCheck EducationContentVersionGovernance);

// No caller-claimed Confirmed field creates clinical rights, verified
// scientific content, participation consent, research consent or publication.
internal static class SyntheticPreventionParticipationGate
{
    internal static SyntheticPreventionReviewResult Assess(
        SyntheticPreventionEvidence input)
    {
        if (input.Track is not (
                SyntheticPreventionTrack.HealthEducation or
                SyntheticPreventionTrack.PreventiveCoordination or
                SyntheticPreventionTrack.LongitudinalClinicalCare) ||
            input.Origin is not (
                SyntheticParticipationOrigin.MemberRequested or
                SyntheticParticipationOrigin.QualifiedClinicianProposed) ||
            input.Purpose is not (
                SyntheticPreventionReviewPurpose.InspectParticipationPrerequisites or
                SyntheticPreventionReviewPurpose.InspectEducationalContentPrerequisites or
                SyntheticPreventionReviewPurpose.StartOrChangeClinicalPlan or
                SyntheticPreventionReviewPurpose.ReuseMemberDataForResearchOrAi) ||
            !Synthetic(input.TenantRef) ||
            !Synthetic(input.MemberRef) ||
            !Synthetic(input.MemberParticipationSubjectRef) ||
            !Synthetic(input.ProposedProgramRef) ||
            !Synthetic(input.ClaimedProposerRef) ||
            !string.Equals(input.MemberRef, input.MemberParticipationSubjectRef,
                StringComparison.Ordinal) ||
            (input.ClaimedResponsibleClinicianRef is not null &&
                !Synthetic(input.ClaimedResponsibleClinicianRef)))
            return SyntheticPreventionReviewResult.InvalidScope;

        var proposerIsMember =
            input.Origin == SyntheticParticipationOrigin.MemberRequested;

        if (proposerIsMember)
        {
            if (input.ClaimedProposerRef != input.MemberRef)
                return SyntheticPreventionReviewResult.ProposerScopeDenied;
        }
        else if (input.ClaimedResponsibleClinicianRef is null ||
                 input.ClaimedResponsibleClinicianRef != input.ClaimedProposerRef ||
                 input.ClaimedProposerRef == input.MemberRef)
            return SyntheticPreventionReviewResult.ProposerScopeDenied;

        // A care request is NEVER a lawful research/training grant and this
        // boundary cannot initiate/change a clinical plan, even with a
        // claimed clinician and entirely Confirmed caller-supplied evidence.
        if (input.Purpose == SyntheticPreventionReviewPurpose.ReuseMemberDataForResearchOrAi)
            return SyntheticPreventionReviewResult.DistinctConsentRequired;
        if (input.Purpose == SyntheticPreventionReviewPurpose.StartOrChangeClinicalPlan)
            return SyntheticPreventionReviewResult.ProhibitedAutomaticAction;
        if (input.Purpose ==
                SyntheticPreventionReviewPurpose.InspectEducationalContentPrerequisites &&
            input.Track != SyntheticPreventionTrack.HealthEducation)
            return SyntheticPreventionReviewResult.InvalidScope;

        // A rejected/withdrawn participation choice is authoritative only as
        // a deny signal in this PURE TEST domain; never infer default opt-in.
        if (input.IndependentMemberParticipationChoice == EvidenceCheck.Rejected)
            return SyntheticPreventionReviewResult.MemberDeclined;

        var checks = new List<EvidenceCheck>
        {
            input.IndependentMemberParticipationChoice,
            input.CurrentMemberIdentityLink,
            input.PurposeSpecificDataBoundary,
            input.LawfulParticipationBasis
        };

        if (input.Track == SyntheticPreventionTrack.HealthEducation)
        {
            // No unreviewed medical advice or licensed-content claim.
            checks.Add(input.EducationScientificReview);
            checks.Add(input.EducationContentRights);
            checks.Add(input.EducationContentVersionGovernance);
        }
        else if (input.EducationScientificReview != EvidenceCheck.Missing ||
                 input.EducationContentRights != EvidenceCheck.Missing ||
                 input.EducationContentVersionGovernance != EvidenceCheck.Missing)
            return SyntheticPreventionReviewResult.InvalidScope;

        var clinicianRequired =
            input.Track == SyntheticPreventionTrack.LongitudinalClinicalCare ||
            input.Origin == SyntheticParticipationOrigin.QualifiedClinicianProposed;
        if (clinicianRequired)
        {
            if (!Synthetic(input.ClaimedResponsibleClinicianRef))
                return SyntheticPreventionReviewResult.ProposerScopeDenied;
            checks.Add(input.ProfessionalQualificationCurrent);
            checks.Add(input.ClinicalResponsibilityCurrent);
        }
        else if (input.ClaimedResponsibleClinicianRef is not null ||
                 input.ProfessionalQualificationCurrent != EvidenceCheck.Missing ||
                 input.ClinicalResponsibilityCurrent != EvidenceCheck.Missing)
            return SyntheticPreventionReviewResult.InvalidScope;

        if (input.Track == SyntheticPreventionTrack.LongitudinalClinicalCare)
            checks.Add(input.ProposedCarePlanHumanReview);
        else if (input.ProposedCarePlanHumanReview != EvidenceCheck.Missing)
            return SyntheticPreventionReviewResult.InvalidScope;

        if (checks.Any(x => x == EvidenceCheck.Rejected))
            return SyntheticPreventionReviewResult.EvidenceRejected;
        if (checks.Any(x => x != EvidenceCheck.Confirmed))
            return SyntheticPreventionReviewResult.EvidenceMissing;

        // ALL checks are untrusted claims. Not an activation or permission.
        return SyntheticPreventionReviewResult.PreconditionsPresentOnly;
    }

    private static bool Synthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
