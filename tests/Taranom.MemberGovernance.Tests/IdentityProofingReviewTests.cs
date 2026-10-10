using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class IdentityProofingReviewTests
{
    private static ProofingReviewInput ValidatedMatch => new(
        ProofingObservation.VerifiedMatch,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        OfficialMembershipAlreadyActivated: false,
        IdentityDataChangedSinceEvidence: false);

    [Fact]
    public void UnconfiguredInputsCannotConfirmIdentity()
    {
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification,
            IdentityProofingReview.Assess(default));
    }

    [Theory]
    [InlineData(ProofingObservation.Missing)]
    [InlineData(ProofingObservation.ProviderUnavailable)]
    [InlineData(ProofingObservation.Inconclusive)]
    [InlineData((ProofingObservation)99)]
    public void UnavailableInconclusiveOrUnknownResultRemainsPending(ProofingObservation result)
    {
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification,
            IdentityProofingReview.Assess(ValidatedMatch with { Observation = result }));
    }

    [Fact]
    public void AValidatedMatchIsEvidenceOnlyNotActivation()
    {
        Assert.Equal(ProofingReviewState.ValidatedProofingEvidenceOnly,
            IdentityProofingReview.Assess(ValidatedMatch));
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing)]
    [InlineData(EvidenceCheck.Rejected)]
    [InlineData((EvidenceCheck)99)]
    public void NonValidatedSourceCannotProveMatch(EvidenceCheck check)
    {
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification,
            IdentityProofingReview.Assess(ValidatedMatch with { ProviderAndEvidenceValidated = check }));
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing)]
    [InlineData(EvidenceCheck.Rejected)]
    public void EvidenceBelongingToDifferentApplicantCannotProveMatch(EvidenceCheck check)
    {
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification,
            IdentityProofingReview.Assess(ValidatedMatch with { EvidenceBelongsToCurrentApplicant = check }));
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing)]
    [InlineData(EvidenceCheck.Rejected)]
    public void StaleOrUnknownEvidenceCannotProveMatch(EvidenceCheck check)
    {
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification,
            IdentityProofingReview.Assess(ValidatedMatch with { EvidenceIsCurrent = check }));
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing)]
    [InlineData(EvidenceCheck.Rejected)]
    [InlineData((EvidenceCheck)25)]
    public void UnknownOfficialActivationStatusCannotGrantPreActivationCorrection(EvidenceCheck check)
    {
        var result = IdentityProofingReview.Assess(ValidatedMatch with
        {
            Observation = ProofingObservation.DefinitiveMismatch,
            OfficialMembershipActivationStatusKnown = check
        });
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification, result);
    }

    [Fact]
    public void ValidatedMismatchBeforeActivationPermitsCorrectionAndReverificationOnly()
    {
        Assert.Equal(ProofingReviewState.CorrectApplicantInformationThenReverify,
            IdentityProofingReview.Assess(ValidatedMatch with { Observation = ProofingObservation.DefinitiveMismatch }));
    }

    [Fact]
    public void IdentityCorrectionInvalidatesPriorSuccessfulEvidence()
    {
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification,
            IdentityProofingReview.Assess(ValidatedMatch with { IdentityDataChangedSinceEvidence = true }));
    }

    [Fact]
    public void IdentityCorrectionInvalidatesPriorMismatchEvidenceToo()
    {
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification,
            IdentityProofingReview.Assess(ValidatedMatch with
            {
                Observation = ProofingObservation.DefinitiveMismatch,
                IdentityDataChangedSinceEvidence = true
            }));
    }

    [Fact]
    public void MismatchForActiveMemberNeedsSeparateIdentityReview()
    {
        Assert.Equal(ProofingReviewState.RequiresSeparateActiveMemberIdentityReview,
            IdentityProofingReview.Assess(ValidatedMatch with
            {
                Observation = ProofingObservation.DefinitiveMismatch,
                OfficialMembershipAlreadyActivated = true
            }));
    }

    [Fact]
    public void CorrectionsForActiveMemberCannotUseApplicantSelfServiceRule()
    {
        Assert.Equal(ProofingReviewState.RequiresSeparateActiveMemberIdentityReview,
            IdentityProofingReview.Assess(ValidatedMatch with
            {
                OfficialMembershipAlreadyActivated = true,
                IdentityDataChangedSinceEvidence = true
            }));
    }

    [Fact]
    public void UnvalidatedMismatchCannotAuthorizeCorrectionAsVerifiedResult()
    {
        Assert.Equal(ProofingReviewState.AwaitingAuthoritativeVerification,
            IdentityProofingReview.Assess(ValidatedMatch with
            {
                Observation = ProofingObservation.DefinitiveMismatch,
                ProviderAndEvidenceValidated = EvidenceCheck.Missing
            }));
    }
}
