using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticSubscriptionPayerBoundaryTests
{
    private static SyntheticSubscriptionPayerInput Input(
        SyntheticMembershipMarket market = SyntheticMembershipMarket.DirectPublic,
        SyntheticSubscriptionPayerKind payer = SyntheticSubscriptionPayerKind.Member,
        SyntheticPayerRequestedCapability capability =
            SyntheticPayerRequestedCapability.InspectSubscriptionFundingEvidence)
    {
        var payerIsMember = payer == SyntheticSubscriptionPayerKind.Member;
        return new(
            market, payer, capability,
            "test:member-one", "test:member-tenant", "test:member-one",
            payerIsMember ? "test:member-one" : "test:sponsor-one",
            payerIsMember ? "test:member-tenant" : "test:sponsor-tenant",
            "test:member-one", "test:member-tenant",
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(3, 3)]
    public void EachContractPermittedMarketAndPayerCombinationOnlyHasClaimedEvidence(
        int market, int payer)
    {
        var result = SyntheticSubscriptionPayerBoundary.Evaluate(
            Input((SyntheticMembershipMarket)market, (SyntheticSubscriptionPayerKind)payer));
        Assert.Equal(SyntheticPayerBoundaryResult.FundingEvidencePresentOnly, result);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    public void WrongPayerForMarketCannotImpersonateAllowedContract(int market, int payer)
    {
        Assert.Equal(SyntheticPayerBoundaryResult.ContractScopeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(
                Input((SyntheticMembershipMarket)market, (SyntheticSubscriptionPayerKind)payer)));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ThirdPartyPayerNeverGetsMemberHealthWalletConsentOrCashAccess(int action)
    {
        var input = Input(SyntheticMembershipMarket.CorporateEmployer,
            SyntheticSubscriptionPayerKind.Employer,
            (SyntheticPayerRequestedCapability)action);
        Assert.Equal(SyntheticPayerBoundaryResult.PayerPrivilegeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(input));
        Assert.Equal(SyntheticPayerBoundaryResult.PayerPrivilegeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(input with
            {
                CurrentContractWithExactMemberAndPayer = EvidenceCheck.Confirmed,
                IndependentlyVerifiedPaymentSource = EvidenceCheck.Confirmed
            }));
    }

    [Fact]
    public void SupportInstitutionIsNotAClinicalOrWalletAuthority()
    {
        var input = Input(SyntheticMembershipMarket.SupportInstitution,
            SyntheticSubscriptionPayerKind.SupportInstitution,
            SyntheticPayerRequestedCapability.ReadMemberHealthRecord);
        Assert.Equal(SyntheticPayerBoundaryResult.PayerPrivilegeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(input));
        Assert.Equal(SyntheticPayerBoundaryResult.PayerPrivilegeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(input with
            {
                RequestedCapability = SyntheticPayerRequestedCapability.ReadMemberPrivateWallet
            }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void MemberActionsStillRequireSeparateAuthorizedDomain(int action)
    {
        Assert.Equal(SyntheticPayerBoundaryResult.SeparateAuthorizationRequired,
            SyntheticSubscriptionPayerBoundary.Evaluate(Input(
                capability: (SyntheticPayerRequestedCapability)action)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ClaimedSubscriptionPaymentNeverActivatesMembership(int payer)
    {
        var market = payer switch
        {
            1 => SyntheticMembershipMarket.DirectPublic,
            2 => SyntheticMembershipMarket.SupportInstitution,
            3 => SyntheticMembershipMarket.CorporateEmployer,
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPayerBoundaryResult.SeparateAuthorizationRequired,
            SyntheticSubscriptionPayerBoundary.Evaluate(Input(market,
                (SyntheticSubscriptionPayerKind)payer,
                SyntheticPayerRequestedCapability.ActivateMemberAccount)));
    }

    [Fact]
    public void CashWithdrawalIsForbiddenForMemberAndSponsor()
    {
        Assert.Equal(SyntheticPayerBoundaryResult.PayerPrivilegeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(Input(
                capability: SyntheticPayerRequestedCapability.WithdrawTreatmentCreditAsCash)));
        Assert.Equal(SyntheticPayerBoundaryResult.PayerPrivilegeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(Input(
                SyntheticMembershipMarket.SupportInstitution,
                SyntheticSubscriptionPayerKind.SupportInstitution,
                SyntheticPayerRequestedCapability.WithdrawTreatmentCreditAsCash)));
    }

    [Theory]
    [InlineData("wallet-owner")]
    [InlineData("contract-member")]
    [InlineData("contract-tenant")]
    [InlineData("payer-actor")]
    public void ExactIndividualOwnershipContractAndClaimedActorCannotBeSwapped(string mutation)
    {
        var sample = Input(SyntheticMembershipMarket.CorporateEmployer,
            SyntheticSubscriptionPayerKind.Employer);
        sample = mutation switch
        {
            "wallet-owner" => sample with { IndividualWalletOwnerRef = "test:employer" },
            "contract-member" => sample with { ClaimedContractMemberRef = "test:other" },
            "contract-tenant" => sample with { ClaimedContractTenantRef = "test:other-tenant" },
            "payer-actor" => sample with { ClaimedPayerRef = "test:member-one" },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPayerBoundaryResult.ContractScopeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(sample));
    }

    [Fact]
    public void MissingPayerTenantIsInvalidInputNotAValidClaimedScope()
    {
        var sample = Input(SyntheticMembershipMarket.CorporateEmployer,
            SyntheticSubscriptionPayerKind.Employer) with
            {
                ClaimedPayerTenantRef = ""
            };
        Assert.Equal(SyntheticPayerBoundaryResult.InvalidInput,
            SyntheticSubscriptionPayerBoundary.Evaluate(sample));
    }

    [Fact]
    public void ClaimedSponsorMayShareTenantButNeverIndividualMemberIdentity()
    {
        // Accepted business policy does not require a different tenant for
        // an employer/support contract; payer and beneficiary still differ.
        var input = Input(SyntheticMembershipMarket.CorporateEmployer,
            SyntheticSubscriptionPayerKind.Employer) with
            {
                ClaimedPayerTenantRef = "test:member-tenant"
            };
        Assert.Equal(SyntheticPayerBoundaryResult.FundingEvidencePresentOnly,
            SyntheticSubscriptionPayerBoundary.Evaluate(input));
        Assert.Equal(SyntheticPayerBoundaryResult.PayerPrivilegeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(input with
            {
                RequestedCapability = SyntheticPayerRequestedCapability.ReadMemberHealthRecord
            }));
    }

    [Fact]
    public void DirectMemberCannotBorrowAnotherPersonPayerReference()
    {
        Assert.Equal(SyntheticPayerBoundaryResult.ContractScopeDenied,
            SyntheticSubscriptionPayerBoundary.Evaluate(Input() with
            {
                ClaimedPayerRef = "test:other-member"
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AllFourIndependentFundingEvidenceChecksAreRequired(int dimension)
    {
        var sample = Input(SyntheticMembershipMarket.SupportInstitution,
            SyntheticSubscriptionPayerKind.SupportInstitution);
        sample = dimension switch
        {
            0 => sample with { CurrentContractWithExactMemberAndPayer = EvidenceCheck.Missing },
            1 => sample with { ContractAllowsClaimedPayerForSubscription = EvidenceCheck.Missing },
            2 => sample with { MembershipSubscriptionFeeEvidence = EvidenceCheck.Missing },
            3 => sample with { IndependentlyVerifiedPaymentSource = EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPayerBoundaryResult.EvidenceMissing,
            SyntheticSubscriptionPayerBoundary.Evaluate(sample));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExplicitFundingRejectionDominatesClaimedCompleteness(int dimension)
    {
        var sample = Input(SyntheticMembershipMarket.CorporateEmployer,
            SyntheticSubscriptionPayerKind.Employer);
        sample = dimension switch
        {
            0 => sample with { CurrentContractWithExactMemberAndPayer = EvidenceCheck.Rejected },
            1 => sample with { ContractAllowsClaimedPayerForSubscription = EvidenceCheck.Rejected },
            2 => sample with { MembershipSubscriptionFeeEvidence = EvidenceCheck.Rejected },
            3 => sample with { IndependentlyVerifiedPaymentSource = EvidenceCheck.Rejected },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPayerBoundaryResult.EvidenceRejected,
            SyntheticSubscriptionPayerBoundary.Evaluate(sample));
    }

    [Fact]
    public void UnknownEvidenceEnumMustNotBecomeConfirmed()
    {
        var sample = Input() with { IndependentlyVerifiedPaymentSource = (EvidenceCheck)87 };
        Assert.Equal(SyntheticPayerBoundaryResult.EvidenceMissing,
            SyntheticSubscriptionPayerBoundary.Evaluate(sample));
    }

    [Theory]
    [InlineData("market")]
    [InlineData("payer")]
    [InlineData("capability")]
    [InlineData("real-member")]
    [InlineData("real-tenant")]
    [InlineData("real-wallet")]
    [InlineData("real-payer")]
    [InlineData("real-contract")]
    [InlineData("whitespace")]
    public void UnknownOrNonSyntheticSourcesCannotReachFundingEvidence(string mutation)
    {
        var sample = Input(SyntheticMembershipMarket.CorporateEmployer,
            SyntheticSubscriptionPayerKind.Employer);
        sample = mutation switch
        {
            "market" => sample with { Market = (SyntheticMembershipMarket)999 },
            "payer" => sample with { PayerKind = (SyntheticSubscriptionPayerKind)999 },
            "capability" => sample with { RequestedCapability = (SyntheticPayerRequestedCapability)999 },
            "real-member" => sample with { MemberRef = "real:person" },
            "real-tenant" => sample with { MemberTenantRef = "real:tenant" },
            "real-wallet" => sample with { IndividualWalletOwnerRef = "person" },
            "real-payer" => sample with { ClaimedPayerRef = "real:employer" },
            "real-contract" => sample with { ClaimedContractMemberRef = "" },
            "whitespace" => sample with { ClaimedPayerTenantRef = "test:bad tenant" },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticPayerBoundaryResult.InvalidInput,
            SyntheticSubscriptionPayerBoundary.Evaluate(sample));
    }

    [Fact]
    public void DefaultInputFailsClosed()
    {
        Assert.Equal(SyntheticPayerBoundaryResult.InvalidInput,
            SyntheticSubscriptionPayerBoundary.Evaluate(default));
    }

    [Fact]
    public void ThisDomainCannotRepresentAnActualWalletOrPaymentMutation()
    {
        string[] forbidden = ["Allowed", "Authorized", "Paid", "Activated",
            "Debited", "Credited", "Granted", "Withdrawn", "ConsentGranted"];
        foreach (var item in Enum.GetNames<SyntheticPayerBoundaryResult>())
            Assert.DoesNotContain(forbidden, word =>
                item.Contains(word, StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(typeof(SyntheticSubscriptionPayerInput).GetProperties(),
            p => p.Name.Contains("Amount") || p.Name.Contains("Balance") ||
                 p.Name.Contains("Bank") || p.Name.Contains("HealthPayload"));
    }
}
