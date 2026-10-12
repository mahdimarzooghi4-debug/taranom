namespace Taranom.MemberGovernance;

// Internal, non-persistent decision vocabulary derived from TRN-BIZ-018…023.
// These are NOT service-provider response codes, DB states or wire DTOs.
public enum ProofingObservation
{
    Missing = 0,
    ProviderUnavailable = 1,
    Inconclusive = 2,
    VerifiedMatch = 3,
    DefinitiveMismatch = 4
}

public enum ProofingReviewState
{
    AwaitingAuthoritativeVerification = 0,
    CorrectApplicantInformationThenReverify = 1,
    RequiresSeparateActiveMemberIdentityReview = 2,
    ValidatedProofingEvidenceOnly = 3
}

public readonly record struct ProofingReviewInput(
    ProofingObservation Observation,
    EvidenceCheck ProviderAndEvidenceValidated,
    EvidenceCheck EvidenceBelongsToCurrentApplicant,
    EvidenceCheck EvidenceIsCurrent,
    EvidenceCheck OfficialMembershipActivationStatusKnown,
    bool OfficialMembershipAlreadyActivated,
    bool IdentityDataChangedSinceEvidence);

// Pure interpretation of *externally* attested evidence supplied by future .NET
// owners. No provider adapter, national-ID data, login, member activation or retry
// schedule is present. Even ValidatedProofingEvidenceOnly grants no rights:
// payment and all other activation requirements must be separately adjudicated.
public static class IdentityProofingReview
{
    public static ProofingReviewState Assess(ProofingReviewInput input)
    {
        // Never assume pre-activation from a missing or invalid status.
        if (input.OfficialMembershipActivationStatusKnown != EvidenceCheck.Confirmed)
            return ProofingReviewState.AwaitingAuthoritativeVerification;

        // No self-service correction may be derived for an already active
        // member; separate authorized identity-change governance is required.
        if (input.OfficialMembershipAlreadyActivated &&
            (input.IdentityDataChangedSinceEvidence ||
             input.Observation == ProofingObservation.DefinitiveMismatch))
            return ProofingReviewState.RequiresSeparateActiveMemberIdentityReview;

        // A positive/mismatch provider claim is not trustworthy until the
        // identity owner verifies source, applicant binding and evidence age.
        if (input.ProviderAndEvidenceValidated != EvidenceCheck.Confirmed ||
            input.EvidenceBelongsToCurrentApplicant != EvidenceCheck.Confirmed ||
            input.EvidenceIsCurrent != EvidenceCheck.Confirmed ||
            input.IdentityDataChangedSinceEvidence)
            return ProofingReviewState.AwaitingAuthoritativeVerification;

        return input.Observation switch
        {
            ProofingObservation.VerifiedMatch => ProofingReviewState.ValidatedProofingEvidenceOnly,
            ProofingObservation.DefinitiveMismatch when !input.OfficialMembershipAlreadyActivated =>
                ProofingReviewState.CorrectApplicantInformationThenReverify,
            _ => ProofingReviewState.AwaitingAuthoritativeVerification
        };
    }
}
