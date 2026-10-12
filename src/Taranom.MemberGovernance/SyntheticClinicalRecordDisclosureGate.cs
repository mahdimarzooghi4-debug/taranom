namespace Taranom.MemberGovernance;

// TRN-BIZ-035: member-controlled data scope AND clinician-specific treatment
// need are independent constraints. INTERNAL / TEST-ONLY: this code does NOT
// grant, inspect, transfer, disclose or retrieve a medical record.
internal enum SyntheticRecordRequester
{
    Clinician = 1,
    Member = 2,
    Coordinator = 3,
    NetworkAffiliate = 4,
    SubscriptionSponsor = 5,
    Employer = 6
}

internal enum SyntheticRecordPurpose
{
    ClinicianTreatmentNeed = 1,
    MemberSelfView = 2,
    ResearchOrAiTraining = 3,
    EmergencyOrLegalException = 4
}

internal enum SyntheticRecordDisclosureResult
{
    InvalidInput = 1,
    ScopeMismatch = 2,
    RequesterDenied = 3,
    SeparateMemberSelfReadRequired = 4,
    SeparateResearchAuthorizationRequired = 5,
    EmergencyExceptionNotEstablished = 6,
    MemberDeclined = 7,
    RestrictionOrWithdrawalUnresolved = 8,
    EvidenceRejected = 9,
    EvidenceMissing = 10,
    PreconditionsPresentOnly = 11
}

// All refs and evidence checks are untrusted synthetic fixture inputs.
// In particular, "approved" denotes a caller-claimed scope, NOT attestation.
// All declared revisions are necessary for safe structural testing only.
internal readonly record struct SyntheticRecordDisclosureInput(
    SyntheticRecordRequester RequesterKind,
    SyntheticRecordPurpose Purpose,
    string? TenantRef,
    string? RecordOwnerTenantRef,
    string? MemberRef,
    string? RecordOwnerMemberRef,
    string? MemberSharingSubjectRef,
    string? ClaimedRequesterRef,
    string? ClaimedClinicianRef,
    string? ClaimedScopedClinicianRef,
    string? ClinicalServiceRef,
    string? ClaimedScopedServiceRef,
    string? RequestedDataCategoryRef,
    string? ClaimedScopedDataCategoryRef,
    string? ClaimedClinicalNeedRef,
    string? UnverifiedRecordSourceRef,
    long RecordSourceRevision,
    long MemberControlRevision,
    long ClinicalNeedRevision,
    EvidenceCheck CurrentMemberSharingChoice,
    EvidenceCheck CurrentWithdrawalAndRestrictionReview,
    EvidenceCheck CurrentIdentityAndSession,
    EvidenceCheck CurrentClinicianProfessionalQualification,
    EvidenceCheck CurrentServiceResponsibility,
    EvidenceCheck ClinicianNeedForThisService,
    EvidenceCheck ExactMemberApprovedDataScope,
    EvidenceCheck CurrentLawfulDisclosureBasis,
    EvidenceCheck DataMinimization,
    EvidenceCheck AuditAndSourceProvenance);

// This is deliberately separate from MemberSelfReadBoundary (self reads)
// and SyntheticSecondaryUseEvidenceGate (research/AI), never replacing either.
// Even every claimed Confirmed check means only prerequisites appear present.
internal static class SyntheticClinicalRecordDisclosureGate
{
    internal static SyntheticRecordDisclosureResult Assess(
        SyntheticRecordDisclosureInput input)
    {
        if (!Enum.IsDefined(input.RequesterKind) ||
            !Enum.IsDefined(input.Purpose) ||
            !IsSynthetic(input.TenantRef) ||
            !IsSynthetic(input.RecordOwnerTenantRef) ||
            !IsSynthetic(input.MemberRef) ||
            !IsSynthetic(input.RecordOwnerMemberRef) ||
            !IsSynthetic(input.MemberSharingSubjectRef) ||
            !IsSynthetic(input.ClaimedRequesterRef) ||
            !IsSynthetic(input.ClaimedClinicianRef) ||
            !IsSynthetic(input.ClaimedScopedClinicianRef) ||
            !IsSynthetic(input.ClinicalServiceRef) ||
            !IsSynthetic(input.ClaimedScopedServiceRef) ||
            !IsSynthetic(input.RequestedDataCategoryRef) ||
            !IsSynthetic(input.ClaimedScopedDataCategoryRef) ||
            !IsSynthetic(input.ClaimedClinicalNeedRef) ||
            !IsSynthetic(input.UnverifiedRecordSourceRef) ||
            input.RecordSourceRevision <= 0 ||
            input.MemberControlRevision <= 0 ||
            input.ClinicalNeedRevision <= 0)
            return SyntheticRecordDisclosureResult.InvalidInput;

        if (input.TenantRef != input.RecordOwnerTenantRef ||
            input.MemberRef != input.RecordOwnerMemberRef ||
            input.MemberRef != input.MemberSharingSubjectRef ||
            input.RequestedDataCategoryRef != input.ClaimedScopedDataCategoryRef ||
            input.ClaimedClinicianRef != input.ClaimedScopedClinicianRef ||
            input.ClinicalServiceRef != input.ClaimedScopedServiceRef ||
            input.ClaimedRequesterRef == input.MemberRef &&
                input.RequesterKind != SyntheticRecordRequester.Member)
            return SyntheticRecordDisclosureResult.ScopeMismatch;

        if (input.Purpose == SyntheticRecordPurpose.MemberSelfView)
            return input.RequesterKind == SyntheticRecordRequester.Member &&
                input.ClaimedRequesterRef == input.MemberRef
                ? SyntheticRecordDisclosureResult.SeparateMemberSelfReadRequired
                : SyntheticRecordDisclosureResult.RequesterDenied;

        if (input.Purpose == SyntheticRecordPurpose.ResearchOrAiTraining)
            return SyntheticRecordDisclosureResult.SeparateResearchAuthorizationRequired;

        if (input.Purpose == SyntheticRecordPurpose.EmergencyOrLegalException)
            return SyntheticRecordDisclosureResult.EmergencyExceptionNotEstablished;

        // No payer, employer, network coordinator, member or provider-affiliate
        // can derive a direct medical-record read from this clinical evidence.
        if (input.RequesterKind != SyntheticRecordRequester.Clinician ||
            input.ClaimedRequesterRef != input.ClaimedClinicianRef ||
            input.ClaimedClinicianRef == input.MemberRef)
            return SyntheticRecordDisclosureResult.RequesterDenied;

        // Negative member restrictions take precedence over any claimed
        // professional access. Withdrawal semantics for actual data use are
        // legally unresolved; this test boundary never continues on a denial.
        if (input.CurrentMemberSharingChoice == EvidenceCheck.Rejected)
            return SyntheticRecordDisclosureResult.MemberDeclined;
        if (input.CurrentWithdrawalAndRestrictionReview == EvidenceCheck.Rejected)
            return SyntheticRecordDisclosureResult.RestrictionOrWithdrawalUnresolved;

        var checks = new[]
        {
            input.CurrentMemberSharingChoice,
            input.CurrentWithdrawalAndRestrictionReview,
            input.CurrentIdentityAndSession,
            input.CurrentClinicianProfessionalQualification,
            input.CurrentServiceResponsibility,
            input.ClinicianNeedForThisService,
            input.ExactMemberApprovedDataScope,
            input.CurrentLawfulDisclosureBasis,
            input.DataMinimization,
            input.AuditAndSourceProvenance
        };
        if (checks.Any(value => value == EvidenceCheck.Rejected))
            return SyntheticRecordDisclosureResult.EvidenceRejected;
        if (checks.Any(value => value != EvidenceCheck.Confirmed))
            return SyntheticRecordDisclosureResult.EvidenceMissing;
        return SyntheticRecordDisclosureResult.PreconditionsPresentOnly;
    }

    private static bool IsSynthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
