namespace Taranom.MemberGovernance;

// TRN-BIZ-028/029/035: A subscription payer is never the member's
// clinical-record, wallet, care-consent or treatment authority.
// This is INTERNAL, PURE and SYNTHETIC-only. Not an authorization service.
internal enum SyntheticMembershipMarket
{
    DirectPublic = 1,
    SupportInstitution = 2,
    CorporateEmployer = 3
}

internal enum SyntheticSubscriptionPayerKind
{
    Member = 1,
    SupportInstitution = 2,
    Employer = 3
}

internal enum SyntheticPayerRequestedCapability
{
    InspectSubscriptionFundingEvidence = 1,
    ActivateMemberAccount = 2,
    ReadMemberHealthRecord = 3,
    ControlMemberTreatmentCredit = 4,
    ChangeMemberClinicalConsent = 5,
    ReadMemberPrivateWallet = 6,
    WithdrawTreatmentCreditAsCash = 7
}

internal enum SyntheticPayerBoundaryResult
{
    InvalidInput = 1,
    ContractScopeDenied = 2,
    PayerPrivilegeDenied = 3,
    EvidenceRejected = 4,
    EvidenceMissing = 5,
    FundingEvidencePresentOnly = 6,
    SeparateAuthorizationRequired = 7
}

internal readonly record struct SyntheticSubscriptionPayerInput(
    SyntheticMembershipMarket Market,
    SyntheticSubscriptionPayerKind PayerKind,
    SyntheticPayerRequestedCapability RequestedCapability,
    string? MemberRef,
    string? MemberTenantRef,
    string? IndividualWalletOwnerRef,
    string? ClaimedPayerRef,
    string? ClaimedPayerTenantRef,
    string? ClaimedContractMemberRef,
    string? ClaimedContractTenantRef,
    EvidenceCheck CurrentContractWithExactMemberAndPayer,
    EvidenceCheck ContractAllowsClaimedPayerForSubscription,
    EvidenceCheck MembershipSubscriptionFeeEvidence,
    EvidenceCheck IndependentlyVerifiedPaymentSource);

// This gate never reads/writes balances, creates memberships or reuses the
// submitted evidence as verified entitlement. Even four Confirmed caller
// claims mean funding evidence PRESENT ONLY, not completed payment.
internal static class SyntheticSubscriptionPayerBoundary
{
    internal static SyntheticPayerBoundaryResult Evaluate(
        SyntheticSubscriptionPayerInput input)
    {
        if (!IsTestRef(input.MemberRef) ||
            !IsTestRef(input.MemberTenantRef) ||
            !IsTestRef(input.IndividualWalletOwnerRef) ||
            !IsTestRef(input.ClaimedPayerRef) ||
            !IsTestRef(input.ClaimedPayerTenantRef) ||
            !IsTestRef(input.ClaimedContractMemberRef) ||
            !IsTestRef(input.ClaimedContractTenantRef) ||
            input.Market is not (
                SyntheticMembershipMarket.DirectPublic or
                SyntheticMembershipMarket.SupportInstitution or
                SyntheticMembershipMarket.CorporateEmployer) ||
            input.PayerKind is not (
                SyntheticSubscriptionPayerKind.Member or
                SyntheticSubscriptionPayerKind.SupportInstitution or
                SyntheticSubscriptionPayerKind.Employer) ||
            input.RequestedCapability is not (
                SyntheticPayerRequestedCapability.InspectSubscriptionFundingEvidence or
                SyntheticPayerRequestedCapability.ActivateMemberAccount or
                SyntheticPayerRequestedCapability.ReadMemberHealthRecord or
                SyntheticPayerRequestedCapability.ControlMemberTreatmentCredit or
                SyntheticPayerRequestedCapability.ChangeMemberClinicalConsent or
                SyntheticPayerRequestedCapability.ReadMemberPrivateWallet or
                SyntheticPayerRequestedCapability.WithdrawTreatmentCreditAsCash))
            return SyntheticPayerBoundaryResult.InvalidInput;

        // The payer's financial contribution MUST NOT change the individual
        // wallet owner's identity or cross the exact beneficiary/tenant.
        if (input.IndividualWalletOwnerRef != input.MemberRef ||
            input.ClaimedContractMemberRef != input.MemberRef ||
            input.ClaimedContractTenantRef != input.MemberTenantRef)
            return SyntheticPayerBoundaryResult.ContractScopeDenied;

        var payerIsMember = input.PayerKind == SyntheticSubscriptionPayerKind.Member;
        if (payerIsMember)
        {
            if (input.ClaimedPayerRef != input.MemberRef ||
                input.ClaimedPayerTenantRef != input.MemberTenantRef)
                return SyntheticPayerBoundaryResult.ContractScopeDenied;
        }
        else if (input.ClaimedPayerRef == input.MemberRef ||
                 input.Market == SyntheticMembershipMarket.DirectPublic ||
                 (input.Market == SyntheticMembershipMarket.SupportInstitution &&
                  input.PayerKind != SyntheticSubscriptionPayerKind.SupportInstitution) ||
                 (input.Market == SyntheticMembershipMarket.CorporateEmployer &&
                  input.PayerKind != SyntheticSubscriptionPayerKind.Employer))
            return SyntheticPayerBoundaryResult.ContractScopeDenied;

        // This is a narrow explicit payer-responsibility contract only:
        // do not allow a sponsor to access member private information
        // even when all contract/payment claims say Confirmed.
        if (input.RequestedCapability ==
            SyntheticPayerRequestedCapability.WithdrawTreatmentCreditAsCash ||
            (!payerIsMember && input.RequestedCapability is (
                SyntheticPayerRequestedCapability.ReadMemberHealthRecord or
                SyntheticPayerRequestedCapability.ControlMemberTreatmentCredit or
                SyntheticPayerRequestedCapability.ChangeMemberClinicalConsent or
                SyntheticPayerRequestedCapability.ReadMemberPrivateWallet)))
            return SyntheticPayerBoundaryResult.PayerPrivilegeDenied;

        // No subscriber, member or sponsor can use this contract alone to
        // activate membership or receive a clinical/financial permission.
        if (input.RequestedCapability !=
            SyntheticPayerRequestedCapability.InspectSubscriptionFundingEvidence)
            return SyntheticPayerBoundaryResult.SeparateAuthorizationRequired;

        var checks = new[]
        {
            input.CurrentContractWithExactMemberAndPayer,
            input.ContractAllowsClaimedPayerForSubscription,
            input.MembershipSubscriptionFeeEvidence,
            input.IndependentlyVerifiedPaymentSource
        };
        if (checks.Any(x => x == EvidenceCheck.Rejected))
            return SyntheticPayerBoundaryResult.EvidenceRejected;
        if (checks.Any(x => x != EvidenceCheck.Confirmed))
            return SyntheticPayerBoundaryResult.EvidenceMissing;

        // A claim is not an attestation: NO fee receipt, membership,
        // treatment-credit grant, health access or finance mutation here.
        return SyntheticPayerBoundaryResult.FundingEvidencePresentOnly;
    }

    private static bool IsTestRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
