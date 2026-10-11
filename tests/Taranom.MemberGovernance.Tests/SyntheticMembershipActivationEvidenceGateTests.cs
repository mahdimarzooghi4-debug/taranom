using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticMembershipActivationEvidenceGateTests
{
    private const string Member = "test:member-a";
    private const string Tenant = "test:tenant-a";
    private static readonly Guid First = Guid.Parse("00000000-0000-4000-8000-000000000047");
    private static readonly Guid CorrectId = Guid.Parse("00000000-0000-4000-8000-000000000048");
    private static readonly Guid ReverifyId = Guid.Parse("00000000-0000-4000-8000-000000000049");

    private static ProofingReviewInput Proof(ProofingObservation observation) =>
        new(observation, EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed, false, false);

    private static ProofingCaseCommand Observe(Guid id, long caseRev,
        long identityRev, ProofingObservation outcome) =>
        new(id, caseRev, identityRev, ProofingCaseAction.ObserveProofing,
            Proof(outcome), EvidenceCheck.Missing, false);

    private static ProofingCaseSnapshot History(
        ProofingObservation initial = ProofingObservation.VerifiedMatch)
    {
        var applied = ProofingCaseJournal.Apply(ProofingCaseSnapshot.Empty,
            Observe(First, 0, 0, initial));
        Assert.Equal(ProofingJournalOutcome.Applied, applied.Outcome);
        return applied.Snapshot;
    }

    private static SyntheticSubscriptionPayerInput Funding(
        SyntheticMembershipMarket market = SyntheticMembershipMarket.DirectPublic,
        SyntheticSubscriptionPayerKind kind = SyntheticSubscriptionPayerKind.Member)
    {
        var payingSelf = kind == SyntheticSubscriptionPayerKind.Member;
        return new(market, kind,
            SyntheticPayerRequestedCapability.InspectSubscriptionFundingEvidence,
            Member, Tenant, Member,
            payingSelf ? Member : "test:third-party-payer",
            payingSelf ? Tenant : "test:external-payer-tenant",
            Member, Tenant,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed);
    }

    private static SyntheticActivationEvidenceInput Input(
        ProofingCaseSnapshot? history = null,
        SyntheticSubscriptionPayerInput? funding = null) =>
        new(Member, Tenant, "test:proofing-case-a",
            (history ?? History()).CaseRevision,
            (history ?? History()).IdentityRevision,
            history ?? History(), EvidenceCheck.Confirmed, false,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, funding ?? Funding());

    [Fact]
    public void CompleteCallerClaimsCanNeverActivateMembershipOrStartWaiting()
    {
        var inspected = SyntheticMembershipActivationEvidenceGate.Inspect(Input());
        Assert.Equal(SyntheticActivationInspection.ClaimedPrerequisitesPresentOnly,
            inspected.Result);
        Assert.Equal(1, inspected.ObservedProofingCaseRevision);
        Assert.Equal(0, inspected.ObservedIdentityRevision);
        Assert.False(inspected.OfficialActivationIssued);
        Assert.False(inspected.WaitingClockStarted);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(3, 3)]
    public void AllApprovedPayerMarketCombinationsStillGrantNoMemberRights(int market, int payer)
    {
        var result = SyntheticMembershipActivationEvidenceGate.Inspect(Input(
            funding: Funding((SyntheticMembershipMarket)market, (SyntheticSubscriptionPayerKind)payer)));
        Assert.Equal(SyntheticActivationInspection.ClaimedPrerequisitesPresentOnly, result.Result);
        Assert.False(result.OfficialActivationIssued);
        Assert.False(result.WaitingClockStarted);
    }

    [Fact]
    public void ADefinitiveMismatchCannotBecomeVerifiedFundingBasedActivation()
    {
        var result = SyntheticMembershipActivationEvidenceGate.Inspect(
            Input(history: History(ProofingObservation.DefinitiveMismatch)));
        Assert.Equal(SyntheticActivationInspection.IdentityCorrectionOrReverificationRequired, result.Result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(0)]
    public void ProviderOutageInconclusiveOrMissingMustNotActivate(int outcome)
    {
        var history = History((ProofingObservation)outcome);
        Assert.Equal(SyntheticActivationInspection.IdentityCorrectionOrReverificationRequired,
            SyntheticMembershipActivationEvidenceGate.Inspect(Input(history: history)).Result);
    }

    [Fact]
    public void CorrectedNationalIdInvalidatesOldProofingUntilReverification()
    {
        var mismatch = History(ProofingObservation.DefinitiveMismatch);
        var correction = new ProofingCaseCommand(
            CorrectId, 1, 0, ProofingCaseAction.ApplicantCorrectedIdentity,
            default, EvidenceCheck.Confirmed, false);
        var corrected = ProofingCaseJournal.Apply(mismatch, correction);
        Assert.Equal(ProofingJournalOutcome.Applied, corrected.Outcome);
        Assert.Equal(SyntheticActivationInspection.IdentityCorrectionOrReverificationRequired,
            SyntheticMembershipActivationEvidenceGate.Inspect(Input(history: corrected.Snapshot)).Result);
        var stale = Input(history: corrected.Snapshot) with { ExpectedIdentityRevision = 0 };
        Assert.Equal(SyntheticActivationInspection.StaleIdentityOrCaseRevision,
            SyntheticMembershipActivationEvidenceGate.Inspect(stale).Result);
        var fresh = ProofingCaseJournal.Apply(corrected.Snapshot,
            Observe(ReverifyId, 2, 1, ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.Applied, fresh.Outcome);
        var view = SyntheticMembershipActivationEvidenceGate.Inspect(Input(history: fresh.Snapshot));
        Assert.Equal(SyntheticActivationInspection.ClaimedPrerequisitesPresentOnly, view.Result);
        Assert.Equal(1, view.ObservedIdentityRevision);
        Assert.False(view.OfficialActivationIssued);
        Assert.False(view.WaitingClockStarted);
    }

    [Fact]
    public void OldCaseVersionFailsClosedEvenAfterAnAccurateNewProofingResult()
    {
        var first = History();
        var second = ProofingCaseJournal.Apply(first,
            Observe(ReverifyId, 1, 0, ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.Applied, second.Outcome);
        var v = Input(history: second.Snapshot) with { ExpectedProofingCaseRevision = 1 };
        Assert.Equal(SyntheticActivationInspection.StaleIdentityOrCaseRevision,
            SyntheticMembershipActivationEvidenceGate.Inspect(v).Result);
    }

    [Theory]
    [InlineData("actual-member")]
    [InlineData("actual-tenant")]
    [InlineData("missing-case")]
    [InlineData("missing-history")]
    [InlineData("negative-identity-version")]
    [InlineData("zero-case-version")]
    [InlineData("unknown-official-status")]
    [InlineData("unknown-basis")]
    public void InvalidSubjectOrEvidenceVocabularyIsDenied(string scenario)
    {
        var v = Input();
        v = scenario switch
        {
            "actual-member" => v with { MemberRef = "person:real" },
            "actual-tenant" => v with { TenantRef = "real:tenant" },
            "missing-case" => v with { UnverifiedProofingCaseRef = null },
            "missing-history" => v with { ProofingHistory = null },
            "negative-identity-version" => v with { ExpectedIdentityRevision = -1 },
            "zero-case-version" => v with { ExpectedProofingCaseRevision = 0 },
            "unknown-official-status" => v with {
                CurrentOfficialActivationStatusKnown = (EvidenceCheck)99 },
            "unknown-basis" => v with { IndependentMemberLegalProcessingBasis = (EvidenceCheck)77 },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticActivationInspection.InvalidInput,
            SyntheticMembershipActivationEvidenceGate.Inspect(v).Result);
    }

    [Fact]
    public void MissingOrEmptyProofingHistoryIsNotProofOfIdentity()
    {
        var v = Input() with {
            ProofingHistory = ProofingCaseSnapshot.Empty,
            ExpectedProofingCaseRevision = 1
        };
        Assert.Equal(SyntheticActivationInspection.ProofingHistoryInvalid,
            SyntheticMembershipActivationEvidenceGate.Inspect(v).Result);
    }

    [Fact]
    public void AlreadyActivatedMemberCannotBeReactivatedViaInspection()
    {
        var v = Input() with { ClaimedCurrentlyActivated = true };
        var x = SyntheticMembershipActivationEvidenceGate.Inspect(v);
        Assert.Equal(SyntheticActivationInspection.AlreadyActiveSeparateAuthority, x.Result);
        Assert.False(x.OfficialActivationIssued);
    }

    [Theory]
    [InlineData("cross-member")]
    [InlineData("cross-tenant")]
    [InlineData("wrong-wallet")]
    [InlineData("attempt-activate")]
    [InlineData("employer-private-record")]
    [InlineData("invalid-payer")]
    [InlineData("bad-payment-contract")]
    public void PayerScopeCannotBecomeActivationAuthority(string scenario)
    {
        var fund = Funding(SyntheticMembershipMarket.CorporateEmployer,
            SyntheticSubscriptionPayerKind.Employer);
        fund = scenario switch
        {
            "cross-member" => fund with { MemberRef = "test:other-person" },
            "cross-tenant" => fund with { MemberTenantRef = "test:other-tenant" },
            "wrong-wallet" => fund with { IndividualWalletOwnerRef = fund.ClaimedPayerRef },
            "attempt-activate" => fund with {
                RequestedCapability = SyntheticPayerRequestedCapability.ActivateMemberAccount },
            "employer-private-record" => fund with {
                RequestedCapability = SyntheticPayerRequestedCapability.ReadMemberHealthRecord },
            "invalid-payer" => fund with { PayerKind = SyntheticSubscriptionPayerKind.Member },
            "bad-payment-contract" => fund with {
                ClaimedContractMemberRef = "test:unrelated-beneficiary" },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticActivationInspection.FundingScopeDenied,
            SyntheticMembershipActivationEvidenceGate.Inspect(Input(funding: fund)).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryOtherActivationEvidenceCategoryIsIndependent(int field)
    {
        var v = WithCheck(Input(), field, EvidenceCheck.Missing);
        var x = SyntheticMembershipActivationEvidenceGate.Inspect(v);
        Assert.Equal(SyntheticActivationInspection.ClaimedEvidenceMissing, x.Result);
        Assert.False(x.OfficialActivationIssued);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ExplicitRejectionOfAnyIndependentCategoryCannotBeOverridden(int field)
    {
        var v = WithCheck(Input(), field, EvidenceCheck.Rejected);
        Assert.Equal(SyntheticActivationInspection.ClaimedEvidenceRejected,
            SyntheticMembershipActivationEvidenceGate.Inspect(v).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ClaimedSubscriptionEvidenceGatesAreIndependentlyMandatory(int field)
    {
        var f = Funding();
        f = field switch
        {
            0 => f with { CurrentContractWithExactMemberAndPayer = EvidenceCheck.Missing },
            1 => f with { ContractAllowsClaimedPayerForSubscription = EvidenceCheck.Missing },
            2 => f with { MembershipSubscriptionFeeEvidence = EvidenceCheck.Missing },
            3 => f with { IndependentlyVerifiedPaymentSource = EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticActivationInspection.ClaimedEvidenceMissing,
            SyntheticMembershipActivationEvidenceGate.Inspect(Input(funding: f)).Result);
    }

    [Fact]
    public void RejectedPayerEvidenceNeverBecomesConfirmedByIdentityMatch()
    {
        var f = Funding() with { IndependentlyVerifiedPaymentSource = EvidenceCheck.Rejected };
        Assert.Equal(SyntheticActivationInspection.ClaimedEvidenceRejected,
            SyntheticMembershipActivationEvidenceGate.Inspect(Input(funding: f)).Result);
    }

    [Fact]
    public void NoContractMayExposeMoneyOrNationalIdentifierOrActivationEvent()
    {
        string[] forbidden = ["NationalId", "Passport", "Phone", "Money", "Amount",
            "PaymentReference", "VerifiedReceipt", "ActivationEvent", "WaitingStartedAt",
            "CreditGranted", "PatientRecord"];
        foreach (var p in typeof(SyntheticActivationEvidenceInput).GetProperties()
            .Concat(typeof(SyntheticActivationEvidenceRead).GetProperties()))
            Assert.DoesNotContain(forbidden, item =>
                p.Name.Contains(item, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Enum.GetNames<SyntheticActivationInspection>(),
            x => x.Contains("Activated") || x.Contains("EligibleForProduction"));
    }

    private static SyntheticActivationEvidenceInput WithCheck(
        SyntheticActivationEvidenceInput v, int field, EvidenceCheck value) =>
        field switch
        {
            0 => v with { CurrentOfficialActivationStatusKnown = value },
            1 => v with { CurrentProofingBelongsToExactMember = value },
            2 => v with { CurrentProofingSourceLicensedAndContracted = value },
            3 => v with { IndependentMemberLegalProcessingBasis = value },
            4 => v with { ExplicitMemberTermsEvidence = value },
            5 => v with { CurrentAllOtherActivationConditionsReviewed = value },
            _ => throw new ArgumentException()
        };
}
