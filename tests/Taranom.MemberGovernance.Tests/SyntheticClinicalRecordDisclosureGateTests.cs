using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticClinicalRecordDisclosureGateTests
{
    private static SyntheticRecordDisclosureInput Valid => new(
        SyntheticRecordRequester.Clinician,
        SyntheticRecordPurpose.ClinicianTreatmentNeed,
        "test:tenant-A", "test:tenant-A",
        "test:member-A", "test:member-A", "test:member-A",
        "test:clinician-A", "test:clinician-A", "test:clinician-A",
        "test:care-service-A", "test:care-service-A",
        "test:category-A", "test:category-A",
        "test:clinical-need-A", "test:record-source-A",
        1, 1, 1,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed);

    [Fact]
    public void MissingDefaultCannotCreateClinicalReadPermission()
    {
        Assert.Equal(SyntheticRecordDisclosureResult.InvalidInput,
            SyntheticClinicalRecordDisclosureGate.Assess(default));
    }

    [Fact]
    public void FullyClaimedClinicalEvidenceIsOnlyUnverifiedPrerequisites()
    {
        Assert.Equal(SyntheticRecordDisclosureResult.PreconditionsPresentOnly,
            SyntheticClinicalRecordDisclosureGate.Assess(Valid));
        Assert.DoesNotContain(Enum.GetNames<SyntheticRecordDisclosureResult>(),
            s => s.Contains("Granted") || s.Contains("Authorized") ||
                 s.Contains("Disclosed") || s.Contains("Permitted"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void NonClinicianCannotInheritClinicalDataRead(int requesterKind)
    {
        var input = Valid with {
            RequesterKind = (SyntheticRecordRequester)requesterKind,
            ClaimedRequesterRef = "test:claimed-external-actor"
        };
        Assert.Equal(SyntheticRecordDisclosureResult.RequesterDenied,
            SyntheticClinicalRecordDisclosureGate.Assess(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void UnknownRequesterIsInvalidNotAuthorized(int kind)
    {
        Assert.Equal(SyntheticRecordDisclosureResult.InvalidInput,
            SyntheticClinicalRecordDisclosureGate.Assess(
                Valid with { RequesterKind = (SyntheticRecordRequester)kind }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void UnknownPurposeIsInvalidNotAuthorized(int purpose)
    {
        Assert.Equal(SyntheticRecordDisclosureResult.InvalidInput,
            SyntheticClinicalRecordDisclosureGate.Assess(
                Valid with { Purpose = (SyntheticRecordPurpose)purpose }));
    }

    [Fact]
    public void MemberSelfViewMustRemainWithExistingMemberSelfReadGate()
    {
        var input = Valid with {
            RequesterKind = SyntheticRecordRequester.Member,
            ClaimedRequesterRef = "test:member-A",
            Purpose = SyntheticRecordPurpose.MemberSelfView
        };
        Assert.Equal(SyntheticRecordDisclosureResult.SeparateMemberSelfReadRequired,
            SyntheticClinicalRecordDisclosureGate.Assess(input));
    }

    [Fact]
    public void ClinicianCannotImpersonateMemberSelfView()
    {
        Assert.Equal(SyntheticRecordDisclosureResult.RequesterDenied,
            SyntheticClinicalRecordDisclosureGate.Assess(
                Valid with { Purpose = SyntheticRecordPurpose.MemberSelfView }));
    }

    [Theory]
    [InlineData(3, 5)]
    [InlineData(4, 6)]
    public void ResearchAndEmergencyPurposesNeverInheritTreatmentScope(
        int purpose, int expectedResult)
    {
        Assert.Equal((SyntheticRecordDisclosureResult)expectedResult,
            SyntheticClinicalRecordDisclosureGate.Assess(
                Valid with { Purpose = (SyntheticRecordPurpose)purpose }));
    }

    [Theory]
    [InlineData("other-tenant")]
    [InlineData("other-member")]
    [InlineData("other-sharing-subject")]
    [InlineData("other-category")]
    [InlineData("other-clinician")]
    [InlineData("other-service")]
    [InlineData("claimed-member-as-clinician")]
    public void CrossScopeOrClinicalNeedSubstitutionFailsClosed(string change)
    {
        var input = change switch
        {
            "other-tenant" => Valid with { RecordOwnerTenantRef = "test:tenant-B" },
            "other-member" => Valid with { RecordOwnerMemberRef = "test:member-B" },
            "other-sharing-subject" => Valid with { MemberSharingSubjectRef = "test:member-B" },
            "other-category" => Valid with { ClaimedScopedDataCategoryRef = "test:category-B" },
            "other-clinician" => Valid with { ClaimedScopedClinicianRef = "test:clinician-B" },
            "other-service" => Valid with { ClaimedScopedServiceRef = "test:service-B" },
            "claimed-member-as-clinician" => Valid with { ClaimedRequesterRef = "test:member-A" },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticRecordDisclosureResult.ScopeMismatch,
            SyntheticClinicalRecordDisclosureGate.Assess(input));
    }

    [Theory]
    [InlineData("different-requester")]
    [InlineData("same-as-member")]
    public void ClinicianClaimMustMatchExactRequestingActor(string change)
    {
        var input = change == "different-requester"
            ? Valid with { ClaimedRequesterRef = "test:clinician-B" }
            : Valid with {
                ClaimedRequesterRef = "test:member-A",
                ClaimedClinicianRef = "test:member-A",
                ClaimedScopedClinicianRef = "test:member-A"
            };
        Assert.NotEqual(SyntheticRecordDisclosureResult.PreconditionsPresentOnly,
            SyntheticClinicalRecordDisclosureGate.Assess(input));
    }

    [Theory]
    [InlineData("member-ref")]
    [InlineData("source-ref")]
    [InlineData("service-ref")]
    [InlineData("category-ref")]
    [InlineData("clinician-ref")]
    [InlineData("need-ref")]
    [InlineData("tenant-ref")]
    public void RealOrMalformedIdentifiersDoNotPass(string field)
    {
        var input = field switch
        {
            "member-ref" => Valid with { MemberRef = "real:member" },
            "source-ref" => Valid with { UnverifiedRecordSourceRef = "patient-record" },
            "service-ref" => Valid with { ClinicalServiceRef = "test:visit/../other" },
            "category-ref" => Valid with { RequestedDataCategoryRef = "test:category 1" },
            "clinician-ref" => Valid with { ClaimedClinicianRef = "test:" },
            "need-ref" => Valid with { ClaimedClinicalNeedRef = "" },
            "tenant-ref" => Valid with { TenantRef = "test:tenant\nA" },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticRecordDisclosureResult.InvalidInput,
            SyntheticClinicalRecordDisclosureGate.Assess(input));
    }

    [Theory]
    [InlineData("record")]
    [InlineData("member-choice")]
    [InlineData("clinical-need")]
    public void EveryClaimedRevisionMustBeExplicitlyPositive(string revision)
    {
        var input = revision switch
        {
            "record" => Valid with { RecordSourceRevision = 0 },
            "member-choice" => Valid with { MemberControlRevision = -1 },
            "clinical-need" => Valid with { ClinicalNeedRevision = 0 },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticRecordDisclosureResult.InvalidInput,
            SyntheticClinicalRecordDisclosureGate.Assess(input));
    }

    [Fact]
    public void ExplicitMemberDeclineDeniesEvenIfClinicianChecksAreConfirmed()
    {
        Assert.Equal(SyntheticRecordDisclosureResult.MemberDeclined,
            SyntheticClinicalRecordDisclosureGate.Assess(
                Valid with { CurrentMemberSharingChoice = EvidenceCheck.Rejected }));
    }

    [Fact]
    public void WithdrawalOrRestrictionCannotBeBypassedByOtherChecks()
    {
        Assert.Equal(SyntheticRecordDisclosureResult.RestrictionOrWithdrawalUnresolved,
            SyntheticClinicalRecordDisclosureGate.Assess(
                Valid with {
                    CurrentWithdrawalAndRestrictionReview = EvidenceCheck.Rejected,
                    CurrentServiceResponsibility = EvidenceCheck.Missing
                }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void AnyMissingPrerequisiteBlocksEvenPartialView(int index)
    {
        var input = ReplaceEvidence(Valid, index, EvidenceCheck.Missing);
        Assert.Equal(SyntheticRecordDisclosureResult.EvidenceMissing,
            SyntheticClinicalRecordDisclosureGate.Assess(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void AnyExplicitNegativePrerequisiteDenies(int index)
    {
        var input = ReplaceEvidence(Valid, index, EvidenceCheck.Rejected);
        var expected = index switch
        {
            0 => SyntheticRecordDisclosureResult.MemberDeclined,
            1 => SyntheticRecordDisclosureResult.RestrictionOrWithdrawalUnresolved,
            _ => SyntheticRecordDisclosureResult.EvidenceRejected
        };
        Assert.Equal(expected, SyntheticClinicalRecordDisclosureGate.Assess(input));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9)]
    public void UnknownFutureEvidenceValueDoesNotFailOpen(int index)
    {
        Assert.Equal(SyntheticRecordDisclosureResult.EvidenceMissing,
            SyntheticClinicalRecordDisclosureGate.Assess(
                ReplaceEvidence(Valid, index, (EvidenceCheck)999)));
    }

    [Fact]
    public void NoPhysicalRecordPayloadOrDisclosureFunctionCanEmerge()
    {
        var fields = typeof(SyntheticRecordDisclosureInput).GetProperties()
            .Select(p => p.Name).ToArray();
        Assert.DoesNotContain(fields, f =>
            f.Contains("MedicalText", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("NationalId", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("Diagnosis", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("PatientData", StringComparison.OrdinalIgnoreCase));
    }

    private static SyntheticRecordDisclosureInput ReplaceEvidence(
        SyntheticRecordDisclosureInput x, int field, EvidenceCheck v) =>
        field switch
        {
            0 => x with { CurrentMemberSharingChoice = v },
            1 => x with { CurrentWithdrawalAndRestrictionReview = v },
            2 => x with { CurrentIdentityAndSession = v },
            3 => x with { CurrentClinicianProfessionalQualification = v },
            4 => x with { CurrentServiceResponsibility = v },
            5 => x with { ClinicianNeedForThisService = v },
            6 => x with { ExactMemberApprovedDataScope = v },
            7 => x with { CurrentLawfulDisclosureBasis = v },
            8 => x with { DataMinimization = v },
            9 => x with { AuditAndSourceProvenance = v },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
}
