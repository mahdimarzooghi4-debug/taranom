using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticCarePreventionContinuityTests
{
    private const string Member = "test:member-a";
    private const string Case = "test:case-a";
    private const string Link = "test:claimed-program-case-link";

    private static SyntheticCareTrailCommand CareRoot() =>
        new(Guid.NewGuid(), 0, SyntheticCareTrailAction.RequestNonclinicalCoordination,
            Case, Member, "test:member-request", Guid.Empty, null, null);

    private static SyntheticCareTrailCommand CareNote(
        SyntheticCareTrailCommand root, SyntheticCareTrailAction action,
        long revision) =>
        root with {
            RequestId = Guid.NewGuid(), ExpectedRevision = revision,
            Action = action, OriginalSubmissionRequestId = root.RequestId,
            ClaimedActorRef = action == SyntheticCareTrailAction.RecordUnverifiedCoordinatorContact
                ? "test:claimed-coordinator" : "test:claimed-document-source",
            UnverifiedNoteOrDocumentRef = action ==
                SyntheticCareTrailAction.RecordUnverifiedCoordinatorContact
                ? "test:claimed-contact-paper" : "test:claimed-clinical-paper"
        };

    private static SyntheticCareTrailSnapshot History(
        bool contact = false, bool clinical = false)
    {
        var root = CareRoot();
        var first = SyntheticCareCoordinationTrail.Apply(
            SyntheticCareTrailSnapshot.Empty, root);
        Assert.Equal(SyntheticCareTrailOutcome.RecordedUnverified, first.Outcome);
        var current = first.Snapshot;
        if (contact)
        {
            var next = SyntheticCareCoordinationTrail.Apply(current,
                CareNote(root, SyntheticCareTrailAction.RecordUnverifiedCoordinatorContact,
                    current.Revision));
            Assert.Equal(SyntheticCareTrailOutcome.RecordedUnverified, next.Outcome);
            current = next.Snapshot;
        }
        if (clinical)
        {
            var next = SyntheticCareCoordinationTrail.Apply(current,
                CareNote(root, SyntheticCareTrailAction.AttachUnverifiedClinicalSourceReference,
                    current.Revision));
            Assert.Equal(SyntheticCareTrailOutcome.RecordedUnverified, next.Outcome);
            current = next.Snapshot;
        }
        return current;
    }

    private static SyntheticPreventionEvidence Prevention(
        SyntheticPreventionTrack track = SyntheticPreventionTrack.HealthEducation,
        SyntheticParticipationOrigin origin = SyntheticParticipationOrigin.MemberRequested,
        SyntheticPreventionReviewPurpose purpose =
            SyntheticPreventionReviewPurpose.InspectParticipationPrerequisites)
    {
        var clinicianProposed = origin ==
            SyntheticParticipationOrigin.QualifiedClinicianProposed;
        var clinical = track == SyntheticPreventionTrack.LongitudinalClinicalCare;
        var clinicianRequired = clinicianProposed || clinical;
        var education = track == SyntheticPreventionTrack.HealthEducation;
        return new(track, origin, purpose,
            "test:tenant-a", Member, Member, "test:program-a",
            clinicianProposed ? "test:clinician-a" : Member,
            clinicianRequired ? "test:clinician-a" : null,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            clinicianRequired ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            clinicianRequired ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            clinical ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            education ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            education ? EvidenceCheck.Confirmed : EvidenceCheck.Missing,
            education ? EvidenceCheck.Confirmed : EvidenceCheck.Missing);
    }

    private static SyntheticCarePreventionContinuityRead Inspect(
        SyntheticCareTrailSnapshot? history,
        SyntheticPreventionEvidence? input = null,
        long? revision = null, string? member = Member, string? caseRef = Case,
        string? link = Link) =>
        SyntheticCarePreventionContinuity.Inspect(history, caseRef, member,
            revision ?? history?.Revision ?? 1, link, input ?? Prevention());

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void AllApprovedPreventionTracksAndProposersRemainUnverified(
        int track, int origin)
    {
        var h = History(clinical: true);
        var read = Inspect(h, Prevention(
            (SyntheticPreventionTrack)track, (SyntheticParticipationOrigin)origin));
        Assert.Equal(SyntheticCarePreventionContinuityState.ClaimedPrerequisitesPresentOnly,
            read.State);
        Assert.Equal((SyntheticPreventionTrack)track, read.Track);
        Assert.Equal((SyntheticParticipationOrigin)origin, read.Origin);
        Assert.Equal(SyntheticPreventionReviewResult.PreconditionsPresentOnly,
            read.UntrustedParticipationInspection);
        Assert.False(read.ParticipationActivated);
        Assert.False(read.EducationPublished);
        Assert.False(read.ClinicalCareStartedOrChanged);
        Assert.False(read.ResearchOrAiConsentGranted);
        Assert.False(read.MedicalRecordDisclosed);
        Assert.False(read.AutomatedFollowUpScheduled);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AllThreeTracksCanRemainPaperOnlyWithoutAClinicalReferral(int track)
    {
        var h = History(contact: true);
        var read = Inspect(h, Prevention((SyntheticPreventionTrack)track));
        Assert.Equal(track == 3
            ? SyntheticCarePreventionContinuityState.UnverifiedClinicalSourceMissing
            : SyntheticCarePreventionContinuityState.ClaimedPrerequisitesPresentOnly,
            read.State);
        Assert.Equal(1, read.UnverifiedCoordinationNoteCount);
        Assert.Equal(0, read.UnverifiedClinicalReferenceCount);
        Assert.False(read.ClinicalCareStartedOrChanged);
    }

    [Fact]
    public void IndependentCareSourceNoteDoesNotEqualProfessionalReviewOrDeliveredCare()
    {
        var h = History(contact:true,clinical:true);
        var read = Inspect(h, Prevention(SyntheticPreventionTrack.LongitudinalClinicalCare));
        Assert.Equal(3, read.CareHistoryRevision);
        Assert.Equal(1, read.UnverifiedCoordinationNoteCount);
        Assert.Equal(1, read.UnverifiedClinicalReferenceCount);
        Assert.False(read.MedicalRecordDisclosed);
        Assert.False(read.ClinicalCareStartedOrChanged);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MemberDeclinesEvenWhenClinicianClaimsAllOtherChecksConfirmed(int track)
    {
        var h=History(clinical:true);
        var input=Prevention((SyntheticPreventionTrack)track,
            SyntheticParticipationOrigin.QualifiedClinicianProposed) with {
                IndependentMemberParticipationChoice=EvidenceCheck.Rejected
            };
        var read=Inspect(h,input);
        Assert.Equal(SyntheticCarePreventionContinuityState.MemberDeclined,read.State);
        Assert.False(read.ParticipationActivated);
    }

    [Theory]
    [InlineData("missing-choice")]
    [InlineData("missing-identity")]
    [InlineData("missing-data-boundary")]
    [InlineData("missing-lawful-basis")]
    [InlineData("missing-education-rights")]
    [InlineData("missing-education-review")]
    [InlineData("missing-content-version")]
    public void MissingIndependentParticipationOrContentAuthorityNeverEnrolsMember(string mode)
    {
        var p=Prevention();
        p=mode switch {
            "missing-choice" => p with { IndependentMemberParticipationChoice=EvidenceCheck.Missing },
            "missing-identity" => p with { CurrentMemberIdentityLink=EvidenceCheck.Missing },
            "missing-data-boundary" => p with { PurposeSpecificDataBoundary=EvidenceCheck.Missing },
            "missing-lawful-basis" => p with { LawfulParticipationBasis=EvidenceCheck.Missing },
            "missing-education-rights" => p with { EducationContentRights=EvidenceCheck.Missing },
            "missing-education-review" => p with { EducationScientificReview=EvidenceCheck.Missing },
            "missing-content-version" => p with { EducationContentVersionGovernance=EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        var result=Inspect(History(),p);
        Assert.Equal(SyntheticCarePreventionContinuityState.ParticipationEvidenceMissing,
            result.State);
        Assert.False(result.EducationPublished);
    }

    [Theory]
    [InlineData("invalid-member")]
    [InlineData("invalid-program")]
    [InlineData("different-subject")]
    [InlineData("invalid-proposer")]
    [InlineData("cross-purpose")]
    public void InvalidParticipationScopeFailsWithoutMemberOrProgramProjection(string mode)
    {
        var p=Prevention();
        p=mode switch {
            "invalid-member"=>p with { MemberRef="real:person" },
            "invalid-program"=>p with { ProposedProgramRef="real:program" },
            "different-subject"=>p with { MemberParticipationSubjectRef="test:other" },
            "invalid-proposer"=>p with { ClaimedProposerRef="test:other" },
            "cross-purpose"=>p with { Purpose=(SyntheticPreventionReviewPurpose)99 },
            _=>throw new ArgumentException()
        };
        var result=Inspect(History(),p);
        Assert.True(result.State is
            SyntheticCarePreventionContinuityState.MemberOrCaseScopeMismatch or
            SyntheticCarePreventionContinuityState.ParticipationScopeInvalid);
        Assert.Null(result.Track);
        Assert.Equal(0,result.CareHistoryRevision);
    }

    [Theory]
    [InlineData("different-member")]
    [InlineData("different-case")]
    [InlineData("care-member")]
    [InlineData("care-case")]
    public void CrossMemberAndCaseLinkageCannotBeInferredFromSyntheticRef(string mode)
    {
        var history=History();
        var input=Prevention();
        var member=mode=="different-member"?"test:member-b":Member;
        var caseRef=mode=="different-case"?"test:case-b":Case;
        if(mode=="care-member") input=input with {
            MemberRef="test:member-b",MemberParticipationSubjectRef="test:member-b",
            ClaimedProposerRef="test:member-b"
        };
        if(mode=="care-case") caseRef="test:unrelated-case";
        var result=Inspect(history,input,member:member,caseRef:caseRef);
        Assert.Equal(SyntheticCarePreventionContinuityState.MemberOrCaseScopeMismatch,
            result.State);
        Assert.False(result.ParticipationActivated);
    }

    [Theory]
    [InlineData("missing-case")]
    [InlineData("missing-member")]
    [InlineData("missing-correspondence")]
    [InlineData("unsafe-correspondence")]
    [InlineData("zero-care-version")]
    public void MalformedOrAbsentLinkFailsClosedWithoutAnyPrivateData(string mode)
    {
        var h=History();
        var result=mode switch {
            "missing-case"=>Inspect(h,caseRef:null),
            "missing-member"=>Inspect(h,member:null),
            "missing-correspondence"=>Inspect(h,link:null),
            "unsafe-correspondence"=>Inspect(h,link:"real:encounter"),
            "zero-care-version"=>Inspect(h,revision:0),
            _=>throw new ArgumentException()
        };
        Assert.Equal(SyntheticCarePreventionContinuityState.InvalidInput,result.State);
        Assert.Null(result.Origin);
        Assert.Equal(0,result.UnverifiedCoordinationNoteCount);
    }

    [Fact]
    public void EmptyCareHistoryNeverBecomesARealCompletedService()
    {
        Assert.Equal(SyntheticCarePreventionContinuityState.UnverifiedCareRequestMissing,
            Inspect(null).State);
        Assert.Equal(SyntheticCarePreventionContinuityState.UnverifiedCareRequestMissing,
            Inspect(SyntheticCareTrailSnapshot.Empty).State);
    }

    [Fact]
    public void StaleRecordedRevisionDoesNotSilentlyUseNewerCareNotes()
    {
        var h=History(contact:true,clinical:true);
        var result=Inspect(h,revision:1);
        Assert.Equal(SyntheticCarePreventionContinuityState.StaleCareHistory,result.State);
        Assert.Null(result.UntrustedParticipationInspection);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NoResearchReuseOrAutomaticTreatmentUnderAnyTrack(int track)
    {
        var h=History(clinical:true);
        foreach(var purpose in new [] {
            SyntheticPreventionReviewPurpose.StartOrChangeClinicalPlan,
            SyntheticPreventionReviewPurpose.ReuseMemberDataForResearchOrAi })
        {
            var result=Inspect(h,Prevention((SyntheticPreventionTrack)track,
                purpose:purpose));
            Assert.Equal(
                SyntheticCarePreventionContinuityState.IndependentConsentOrClinicianDecisionRequired,
                result.State);
            Assert.False(result.ResearchOrAiConsentGranted);
            Assert.False(result.ClinicalCareStartedOrChanged);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingClinicianEvidenceForLongitudinalCareNeverBecomesApprovedPlan(int which)
    {
        var p=Prevention(SyntheticPreventionTrack.LongitudinalClinicalCare);
        p=which switch {
            0=>p with { ProfessionalQualificationCurrent=EvidenceCheck.Missing },
            1=>p with { ClinicalResponsibilityCurrent=EvidenceCheck.Missing },
            2=>p with { ProposedCarePlanHumanReview=EvidenceCheck.Missing },
            _=>throw new ArgumentException()
        };
        var r=Inspect(History(clinical:true),p);
        Assert.Equal(SyntheticCarePreventionContinuityState.ParticipationEvidenceMissing,r.State);
        Assert.False(r.ClinicalCareStartedOrChanged);
    }

    [Fact]
    public void ExplicitlyRejectedProfessionalQualificationCannotBeReplacedByCoordinator()
    {
        var p=Prevention(SyntheticPreventionTrack.LongitudinalClinicalCare) with {
            ProfessionalQualificationCurrent=EvidenceCheck.Rejected
        };
        var r=Inspect(History(contact:true,clinical:true),p);
        Assert.Equal(SyntheticCarePreventionContinuityState.ParticipationEvidenceRejected,r.State);
        Assert.False(r.ClinicalCareStartedOrChanged);
    }

    [Fact]
    public void OriginalCareReducerReplaysMultipleHistoryItemsAsTechnicalPaperOnly()
    {
        var h=History(contact:true,clinical:true);
        var r=Inspect(h);
        Assert.Equal(SyntheticCarePreventionContinuityState.ClaimedPrerequisitesPresentOnly,r.State);
        Assert.Equal(h.Revision,r.CareHistoryRevision);
        Assert.False(r.AutomatedFollowUpScheduled);
    }

    [Fact]
    public void TestOnlyReadModelHasNoPatientPayloadPaymentOrLicensedActor()
    {
        var forbidden=new[] {"Diagnosis","RecordPayload","ClinicalText","Amount",
            "WalletBalance","SignedConsent","Prescription","FollowUpDate","PatientName",
            "AppointmentId","ProviderLicense","MedicalRecord"};
        foreach(var property in typeof(SyntheticCarePreventionContinuityRead)
            .GetProperties().Select(x=>x.Name))
            Assert.DoesNotContain(forbidden,p=>property.Contains(p,StringComparison.OrdinalIgnoreCase));
        var response=Inspect(History(clinical:true));
        Assert.False(response.ParticipationActivated);
        Assert.False(response.MedicalRecordDisclosed);
    }
}
