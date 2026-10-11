namespace Taranom.ClinicalCenterGovernance;

// TRN-BIZ-043/044: direct-provider and partner-network pathways are
// distinct. INTERNAL TEST-ONLY structural review; no actual licences,
// professional verification, clinical authorization or financial movement.
internal enum SyntheticProviderPath
{
    DirectContract = 1,
    QualifiedPartnerNetwork = 2
}

internal enum SyntheticProviderEvidencePurpose
{
    InspectProviderQualification = 1,
    InspectProposedTreatmentCreditRecipient = 2
}

internal enum SyntheticProviderEvidenceResult
{
    InvalidScope = 1,
    NetworkScopeDenied = 2,
    PaymentRecipientContractMismatch = 3,
    EvidenceRejected = 4,
    EvidenceMissing = 5,
    PreconditionsPresentOnly = 6
}

internal readonly record struct SyntheticProviderNetworkReview(
    SyntheticProviderPath Path,
    SyntheticProviderEvidencePurpose Purpose,
    string? TenantRef,
    string? ProposedProviderRef,
    string? ClinicalResponsibilityProviderRef,
    string? ProposedServiceScopeRef,
    string? ProposedPayeeRef,
    string? ClaimedDirectTaranomContractPayeeRef,
    string? ClaimedPartnerNetworkRef,
    string? ClaimedNetworkMemberProviderRef,
    CenterEvidence ProviderProfessionalCredentialCurrent,
    CenterEvidence ProviderServiceScopeValid,
    CenterEvidence ProviderLegalResponsibilityEvidence,
    CenterEvidence CurrentProviderQualityOversight,
    CenterEvidence PartnerNetworkQualified,
    CenterEvidence PartnerNetworkContractAndDelegation,
    CenterEvidence PartnerNetworkProviderControl,
    CenterEvidence DirectTaranomPayeeContractCurrent);

// A completed array of caller-claimed evidence CANNOT enroll a real
// provider, authorize a medical service, direct a payment, or grant data access.
// Payee must be bound to a direct Taranom contract even on network route.
internal static class SyntheticProviderNetworkEvidenceGate
{
    internal static SyntheticProviderEvidenceResult Assess(
        SyntheticProviderNetworkReview input)
    {
        if (input.Path is not (
                SyntheticProviderPath.DirectContract or
                SyntheticProviderPath.QualifiedPartnerNetwork) ||
            input.Purpose is not (
                SyntheticProviderEvidencePurpose.InspectProviderQualification or
                SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient) ||
            !ClinicalCenterJournal.IsSyntheticRef(input.TenantRef) ||
            !ClinicalCenterJournal.IsSyntheticRef(input.ProposedProviderRef) ||
            !ClinicalCenterJournal.IsSyntheticRef(input.ClinicalResponsibilityProviderRef) ||
            !ClinicalCenterJournal.IsSyntheticRef(input.ProposedServiceScopeRef) ||
            !string.Equals(input.ProposedProviderRef,
                input.ClinicalResponsibilityProviderRef, StringComparison.Ordinal))
            return SyntheticProviderEvidenceResult.InvalidScope;

        if (input.Path == SyntheticProviderPath.DirectContract)
        {
            if (input.ClaimedPartnerNetworkRef is not null ||
                input.ClaimedNetworkMemberProviderRef is not null)
                return SyntheticProviderEvidenceResult.NetworkScopeDenied;
        }
        else if (!ClinicalCenterJournal.IsSyntheticRef(input.ClaimedPartnerNetworkRef) ||
                 !ClinicalCenterJournal.IsSyntheticRef(input.ClaimedNetworkMemberProviderRef) ||
                 input.ClaimedNetworkMemberProviderRef != input.ProposedProviderRef)
            return SyntheticProviderEvidenceResult.NetworkScopeDenied;

        if (input.Purpose ==
            SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient)
        {
            // Do not assume the individual professional and legal payee are
            // the same entity. Payee MUST match its own Taranom contract party.
            if (!ClinicalCenterJournal.IsSyntheticRef(input.ProposedPayeeRef) ||
                !ClinicalCenterJournal.IsSyntheticRef(input.ClaimedDirectTaranomContractPayeeRef) ||
                input.ProposedPayeeRef != input.ClaimedDirectTaranomContractPayeeRef)
                return SyntheticProviderEvidenceResult.PaymentRecipientContractMismatch;
        }
        else if (input.ProposedPayeeRef is not null ||
                 input.ClaimedDirectTaranomContractPayeeRef is not null)
            return SyntheticProviderEvidenceResult.InvalidScope;

        var checks = new List<CenterEvidence>
        {
            input.ProviderProfessionalCredentialCurrent,
            input.ProviderServiceScopeValid,
            input.ProviderLegalResponsibilityEvidence,
            input.CurrentProviderQualityOversight
        };

        if (input.Path == SyntheticProviderPath.QualifiedPartnerNetwork)
        {
            checks.Add(input.PartnerNetworkQualified);
            checks.Add(input.PartnerNetworkContractAndDelegation);
            checks.Add(input.PartnerNetworkProviderControl);
        }
        else if (input.PartnerNetworkQualified != CenterEvidence.Missing ||
                 input.PartnerNetworkContractAndDelegation != CenterEvidence.Missing ||
                 input.PartnerNetworkProviderControl != CenterEvidence.Missing)
            return SyntheticProviderEvidenceResult.NetworkScopeDenied;

        if (input.Purpose ==
            SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient)
            checks.Add(input.DirectTaranomPayeeContractCurrent);
        else if (input.DirectTaranomPayeeContractCurrent != CenterEvidence.Missing)
            return SyntheticProviderEvidenceResult.InvalidScope;

        if (checks.Any(x => x == CenterEvidence.Rejected))
            return SyntheticProviderEvidenceResult.EvidenceRejected;
        if (checks.Any(x => x != CenterEvidence.Confirmed))
            return SyntheticProviderEvidenceResult.EvidenceMissing;

        // All statements originate from untrusted test-only inputs.
        return SyntheticProviderEvidenceResult.PreconditionsPresentOnly;
    }
}
