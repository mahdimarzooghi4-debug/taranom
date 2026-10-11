using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticPreventionParticipationGateTests
{
    private static SyntheticPreventionEvidence Input(
        SyntheticPreventionTrack track = SyntheticPreventionTrack.HealthEducation,
        SyntheticParticipationOrigin origin = SyntheticParticipationOrigin.MemberRequested,
        SyntheticPreventionReviewPurpose purpose =
            SyntheticPreventionReviewPurpose.InspectParticipationPrerequisites)
    {
        var doctorProposed = origin == SyntheticParticipationOrigin.QualifiedClinicianProposed;
        var clinicalTrack = track == SyntheticPreventionTrack.LongitudinalClinicalCare;
        var clinicianRequired = doctorProposed || clinicalTrack;
        var education = track == SyntheticPreventionTrack.HealthEducation;
        return new(
            track, origin, purpose,
            "test:tenant-a", "test:member-a", "test:member-a", "test:program-a",
            doctorProposed ? "test:clinician-a" : "test:member-a",
            clinicianRequired ? "test:clinician-a" : null,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed,
            clinicianRequired ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            clinicianRequired ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            clinicalTrack ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            education ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            education ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            education ? EvidenceCheck.Confirmed : EvidenceCheck.Missing);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void BothOriginsAndAllThreeTracksRemainOnlyUnverifiedPrerequisites(
        int track, int origin)
    {
        Assert.Equal(SyntheticPreventionReviewResult.PreconditionsPresentOnly,
            SyntheticPreventionParticipationGate.Assess(Input(
                (SyntheticPreventionTrack)track, (SyntheticParticipationOrigin)origin)));
    }

    [Fact]
    public void ReviewedEducationalMaterialStillCannotBePublished()
    {
        Assert.Equal(SyntheticPreventionReviewResult.PreconditionsPresentOnly,
            SyntheticPreventionParticipationGate.Assess(Input(
                purpose: SyntheticPreventionReviewPurpose.InspectEducationalContentPrerequisites)));
        Assert.DoesNotContain(Enum.GetNames<SyntheticPreventionReviewResult>(),
            name => name.Contains("Published", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NoTrackCanAutoStartOrChangeCarePlans(int track)
    {
        var sample = Input((SyntheticPreventionTrack)track,
            SyntheticParticipationOrigin.QualifiedClinicianProposed,
            SyntheticPreventionReviewPurpose.StartOrChangeClinicalPlan);
        Assert.Equal(SyntheticPreventionReviewResult.ProhibitedAutomaticAction,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ParticipationCannotBeReusedAsAiOrResearchConsent(int track)
    {
        var sample = Input((SyntheticPreventionTrack)track,
            SyntheticParticipationOrigin.MemberRequested,
            SyntheticPreventionReviewPurpose.ReuseMemberDataForResearchOrAi);
        Assert.Equal(SyntheticPreventionReviewResult.DistinctConsentRequired,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExplicitlyDeclinedParticipationAlwaysDenyRegardlessOfProposer(int track)
    {
        var sample = Input((SyntheticPreventionTrack)track,
            SyntheticParticipationOrigin.QualifiedClinicianProposed) with
            { IndependentMemberParticipationChoice = EvidenceCheck.Rejected };
        Assert.Equal(SyntheticPreventionReviewResult.MemberDeclined,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MemberIdentityPurposeAndLawfulBasisAreIndependentEvidence(int i)
    {
        var sample = Input();
        sample = i switch
        {
            0 => sample with { IndependentMemberParticipationChoice = EvidenceCheck.Missing },
            1 => sample with { CurrentMemberIdentityLink = EvidenceCheck.Missing },
            2 => sample with { PurposeSpecificDataBoundary = EvidenceCheck.Missing },
            3 => sample with { LawfulParticipationBasis = EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPreventionReviewResult.EvidenceMissing,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EducationScientificReviewRightsAndVersionNeedIndependentEvidence(int i)
    {
        var sample = Input();
        sample = i switch
        {
            0 => sample with { EducationScientificReview = EvidenceCheck.Missing },
            1 => sample with { EducationContentRights = EvidenceCheck.Missing },
            2 => sample with { EducationContentVersionGovernance = EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPreventionReviewResult.EvidenceMissing,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ClinicalCareRequiresHumanProfessionalAndCarePlanReview(int i)
    {
        var sample = Input(SyntheticPreventionTrack.LongitudinalClinicalCare);
        sample = i switch
        {
            0 => sample with { ProfessionalQualificationCurrent = EvidenceCheck.Missing },
            1 => sample with { ClinicalResponsibilityCurrent = EvidenceCheck.Missing },
            2 => sample with { ProposedCarePlanHumanReview = EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPreventionReviewResult.EvidenceMissing,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ClinicianProposedEducationStillRequiresCredentialAndResponsibility(int i)
    {
        var sample = Input(SyntheticPreventionTrack.HealthEducation,
            SyntheticParticipationOrigin.QualifiedClinicianProposed);
        sample = i switch
        {
            0 => sample with { ProfessionalQualificationCurrent = EvidenceCheck.Missing },
            1 => sample with { ClinicalResponsibilityCurrent = EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPreventionReviewResult.EvidenceMissing,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Fact]
    public void NegativeContentRightsOverrideOtherUnverifiedEvidence()
    {
        var sample = Input() with {
            EducationContentRights = EvidenceCheck.Rejected,
            EducationScientificReview = EvidenceCheck.Missing
        };
        Assert.Equal(SyntheticPreventionReviewResult.EvidenceRejected,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Fact]
    public void NegativeClinicalQualificationOverridesUnverifiedHumanPlan()
    {
        var sample = Input(SyntheticPreventionTrack.LongitudinalClinicalCare) with {
            ProfessionalQualificationCurrent = EvidenceCheck.Rejected,
            ProposedCarePlanHumanReview = EvidenceCheck.Missing
        };
        Assert.Equal(SyntheticPreventionReviewResult.EvidenceRejected,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Fact]
    public void RejectedMemberChoiceDominatesReportedContentApproval()
    {
        var sample = Input() with { IndependentMemberParticipationChoice = EvidenceCheck.Rejected };
        Assert.Equal(SyntheticPreventionReviewResult.MemberDeclined,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData("member-actor-swapped")]
    [InlineData("clinician-actor-swapped")]
    [InlineData("missing-clinician")]
    [InlineData("fake-clinician")]
    public void DistinctMemberAndClinicianOriginActorsCannotBeForged(string mode)
    {
        var sample = mode switch
        {
            "member-actor-swapped" => Input() with
                { ClaimedProposerRef = "test:clinician-a" },
            "clinician-actor-swapped" => Input(
                origin: SyntheticParticipationOrigin.QualifiedClinicianProposed) with
                { ClaimedProposerRef = "test:member-a" },
            "missing-clinician" => Input(
                origin: SyntheticParticipationOrigin.QualifiedClinicianProposed) with
                { ClaimedResponsibleClinicianRef = null },
            "fake-clinician" => Input(
                origin: SyntheticParticipationOrigin.QualifiedClinicianProposed) with
                { ClaimedResponsibleClinicianRef = "test:other-clinician" },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPreventionReviewResult.ProposerScopeDenied,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Fact]
    public void MemberInitiatedClinicalCareNeedsASeparateClaimedClinician()
    {
        var sample = Input(SyntheticPreventionTrack.LongitudinalClinicalCare)
            with { ClaimedResponsibleClinicianRef = null };
        Assert.Equal(SyntheticPreventionReviewResult.ProposerScopeDenied,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("consent-subject")]
    [InlineData("tenant")]
    [InlineData("program")]
    [InlineData("proposer")]
    [InlineData("cross-member")]
    [InlineData("real-clinician")]
    [InlineData("future-track")]
    [InlineData("future-origin")]
    [InlineData("future-purpose")]
    public void MissingRealOrCrossScopedIdentifiersAndUnknownVocabulariesFailClosed(string mode)
    {
        var sample = Input();
        sample = mode switch
        {
            "member" => sample with { MemberRef = "person-real" },
            "consent-subject" => sample with { MemberParticipationSubjectRef = null },
            "tenant" => sample with { TenantRef = "real:tenant" },
            "program" => sample with { ProposedProgramRef = "https://example.invalid" },
            "proposer" => sample with { ClaimedProposerRef = "member-a" },
            "cross-member" => sample with { MemberParticipationSubjectRef = "test:member-b" },
            "real-clinician" => sample with { ClaimedResponsibleClinicianRef = "real:doctor" },
            "future-track" => sample with { Track = (SyntheticPreventionTrack)90 },
            "future-origin" => sample with { Origin = (SyntheticParticipationOrigin)90 },
            "future-purpose" => sample with { Purpose = (SyntheticPreventionReviewPurpose)90 },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPreventionReviewResult.InvalidScope,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Fact]
    public void FutureEvidenceEnumsCannotBecomeImplicitConsent()
    {
        var sample = Input() with { IndependentMemberParticipationChoice = (EvidenceCheck)92 };
        Assert.Equal(SyntheticPreventionReviewResult.EvidenceMissing,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ContentReviewPurposeCannotBeUsedForNonEducationPrograms(int track)
    {
        var sample = Input((SyntheticPreventionTrack)track,
            purpose: SyntheticPreventionReviewPurpose.InspectEducationalContentPrerequisites);
        Assert.Equal(SyntheticPreventionReviewResult.InvalidScope,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Fact]
    public void GeneralEducationCannotSmuggleClaimedClinicalCareEvidence()
    {
        var sample = Input() with
        { ClinicalResponsibilityCurrent = EvidenceCheck.Confirmed };
        Assert.Equal(SyntheticPreventionReviewResult.InvalidScope,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Fact]
    public void PreventiveCoordinationCannotSmuggleClinicalPlanOrEducationContentEvidence()
    {
        var sample = Input(SyntheticPreventionTrack.PreventiveCoordination) with
        { ProposedCarePlanHumanReview = EvidenceCheck.Confirmed };
        Assert.Equal(SyntheticPreventionReviewResult.InvalidScope,
            SyntheticPreventionParticipationGate.Assess(sample));
        sample = Input(SyntheticPreventionTrack.PreventiveCoordination) with
        { EducationScientificReview = EvidenceCheck.Confirmed };
        Assert.Equal(SyntheticPreventionReviewResult.InvalidScope,
            SyntheticPreventionParticipationGate.Assess(sample));
    }

    [Fact]
    public void NoResultAllowsPublishingMemberDataOrClinicalTreatment()
    {
        foreach (var result in Enum.GetNames<SyntheticPreventionReviewResult>())
            Assert.DoesNotContain(new[] {"Authorized", "Published", "CareStarted",
                "ConsentGranted", "ProgramActivated", "TrainingAllowed", "ClinicallyApproved"},
                label => result.Contains(label, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(SyntheticPreventionEvidence).GetProperties(),
            x => x.Name.Contains("Diagnosis") || x.Name.Contains("Money") ||
                 x.Name.Contains("HealthRecordPayload"));
    }
}
