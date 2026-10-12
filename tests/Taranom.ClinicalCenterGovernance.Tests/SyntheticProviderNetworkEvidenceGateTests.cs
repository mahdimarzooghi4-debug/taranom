using Taranom.ClinicalCenterGovernance;
using Xunit;

namespace Taranom.ClinicalCenterGovernance.Tests;

public sealed class SyntheticProviderNetworkEvidenceGateTests
{
    private static SyntheticProviderNetworkReview Input(
        SyntheticProviderPath path = SyntheticProviderPath.DirectContract,
        SyntheticProviderEvidencePurpose purpose =
            SyntheticProviderEvidencePurpose.InspectProviderQualification)
    {
        var network = path == SyntheticProviderPath.QualifiedPartnerNetwork;
        var payment = purpose ==
            SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient;
        return new(
            path, purpose, "test:tenant-a", "test:provider-a", "test:provider-a",
            "test:service-a",
            payment ? "test:legal-payee" : null,
            payment ? "test:legal-payee" : null,
            network ? "test:network-a" : null,
            network ? "test:provider-a" : null,
            CenterEvidence.Confirmed,
            CenterEvidence.Confirmed,
            CenterEvidence.Confirmed,
            CenterEvidence.Confirmed,
            network ? CenterEvidence.Confirmed : CenterEvidence.Missing,
            network ? CenterEvidence.Confirmed : CenterEvidence.Missing,
            network ? CenterEvidence.Confirmed : CenterEvidence.Missing,
            payment ? CenterEvidence.Confirmed : CenterEvidence.Missing);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void AllFourPathAndPurposeCombinationsNeverApproveActualProviderOrPayee(
        int path, int purpose)
    {
        Assert.Equal(SyntheticProviderEvidenceResult.PreconditionsPresentOnly,
            SyntheticProviderNetworkEvidenceGate.Assess(Input(
                (SyntheticProviderPath)path,
                (SyntheticProviderEvidencePurpose)purpose)));
    }

    [Fact]
    public void APartnerNetworkDoesNotReplaceDirectTaranomPayeeContract()
    {
        var sample = Input(SyntheticProviderPath.QualifiedPartnerNetwork,
            SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient)
            with { DirectTaranomPayeeContractCurrent = CenterEvidence.Missing };
        Assert.Equal(SyntheticProviderEvidenceResult.EvidenceMissing,
            SyntheticProviderNetworkEvidenceGate.Assess(sample));
    }

    [Fact]
    public void PartnerCannotRedirectMemberCreditToItsNetworkInsteadOfContractedPayee()
    {
        var sample = Input(SyntheticProviderPath.QualifiedPartnerNetwork,
            SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient) with
            {
                ProposedPayeeRef = "test:network-a",
                ClaimedDirectTaranomContractPayeeRef = "test:legal-payee"
            };
        Assert.Equal(SyntheticProviderEvidenceResult.PaymentRecipientContractMismatch,
            SyntheticProviderNetworkEvidenceGate.Assess(sample));
    }

    [Fact]
    public void LegalPayeeAndClinicalProfessionalDoNotHaveToBeSameEntity()
    {
        var sample = Input(SyntheticProviderPath.DirectContract,
            SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient);
        Assert.NotEqual(sample.ProposedProviderRef, sample.ProposedPayeeRef);
        Assert.Equal(SyntheticProviderEvidenceResult.PreconditionsPresentOnly,
            SyntheticProviderNetworkEvidenceGate.Assess(sample));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ProviderProfessionalScopeResponsibilityAndQualityChecksAreIndependent(
        int dimension)
    {
        var x = Input(SyntheticProviderPath.DirectContract);
        x = dimension switch
        {
            0 => x with { ProviderProfessionalCredentialCurrent = CenterEvidence.Missing },
            1 => x with { ProviderServiceScopeValid = CenterEvidence.Missing },
            2 => x with { ProviderLegalResponsibilityEvidence = CenterEvidence.Missing },
            3 => x with { CurrentProviderQualityOversight = CenterEvidence.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderEvidenceResult.EvidenceMissing,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NetworkProfessionalDelegationAndControlAreIndependent(
        int dimension)
    {
        var x = Input(SyntheticProviderPath.QualifiedPartnerNetwork);
        x = dimension switch
        {
            0 => x with { PartnerNetworkQualified = CenterEvidence.Missing },
            1 => x with { PartnerNetworkContractAndDelegation = CenterEvidence.Missing },
            2 => x with { PartnerNetworkProviderControl = CenterEvidence.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderEvidenceResult.EvidenceMissing,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExplicitProviderRejectionOverridesUnknownOtherEvidence(
        int dimension)
    {
        var x = Input(SyntheticProviderPath.QualifiedPartnerNetwork) with
        {
            PartnerNetworkQualified = CenterEvidence.Missing
        };
        x = dimension switch
        {
            0 => x with { ProviderProfessionalCredentialCurrent = CenterEvidence.Rejected },
            1 => x with { ProviderServiceScopeValid = CenterEvidence.Rejected },
            2 => x with { ProviderLegalResponsibilityEvidence = CenterEvidence.Rejected },
            3 => x with { CurrentProviderQualityOversight = CenterEvidence.Rejected },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderEvidenceResult.EvidenceRejected,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExplicitNetworkRejectionOverridesOtherMissingEvidence(int dimension)
    {
        var x = Input(SyntheticProviderPath.QualifiedPartnerNetwork) with
        {
            ProviderProfessionalCredentialCurrent = CenterEvidence.Missing
        };
        x = dimension switch
        {
            0 => x with { PartnerNetworkQualified = CenterEvidence.Rejected },
            1 => x with { PartnerNetworkContractAndDelegation = CenterEvidence.Rejected },
            2 => x with { PartnerNetworkProviderControl = CenterEvidence.Rejected },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderEvidenceResult.EvidenceRejected,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Theory]
    [InlineData("missing-network")]
    [InlineData("wrong-network-provider")]
    [InlineData("real-network")]
    [InlineData("unexpected-direct-network")]
    [InlineData("unexpected-direct-control")]
    public void CrossNetworkClaimOrDirectPathNetworkDataFailClosed(string mode)
    {
        var x = Input(SyntheticProviderPath.QualifiedPartnerNetwork);
        x = mode switch
        {
            "missing-network" => x with { ClaimedPartnerNetworkRef = null },
            "wrong-network-provider" => x with
                { ClaimedNetworkMemberProviderRef = "test:another-provider" },
            "real-network" => x with { ClaimedPartnerNetworkRef = "real:network" },
            "unexpected-direct-network" => Input() with
                { ClaimedPartnerNetworkRef = "test:network-a" },
            "unexpected-direct-control" => Input() with
                { PartnerNetworkProviderControl = CenterEvidence.Confirmed },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderEvidenceResult.NetworkScopeDenied,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Theory]
    [InlineData("payee-change")]
    [InlineData("contract-party-change")]
    [InlineData("missing-contract-party")]
    [InlineData("real-payee")]
    public void PayeeIsPinnedToItsOwnSpecificTaranomContract(string mode)
    {
        var x = Input(SyntheticProviderPath.QualifiedPartnerNetwork,
            SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient);
        x = mode switch
        {
            "payee-change" => x with { ProposedPayeeRef = "test:alternate" },
            "contract-party-change" => x with
                { ClaimedDirectTaranomContractPayeeRef = "test:alternate" },
            "missing-contract-party" => x with
                { ClaimedDirectTaranomContractPayeeRef = null },
            "real-payee" => x with { ProposedPayeeRef = "real:payee" },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderEvidenceResult.PaymentRecipientContractMismatch,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Theory]
    [InlineData("cross-tenant")]
    [InlineData("provider-mismatch")]
    [InlineData("missing-service")]
    [InlineData("real-provider")]
    [InlineData("unknown-path")]
    [InlineData("unknown-purpose")]
    [InlineData("default")]
    public void InvalidClinicalScopeCannotBeQualified(string mode)
    {
        var x = Input();
        x = mode switch
        {
            "cross-tenant" => x with { TenantRef = "real:tenant" },
            "provider-mismatch" => x with
                { ClinicalResponsibilityProviderRef = "test:another-provider" },
            "missing-service" => x with { ProposedServiceScopeRef = null },
            "real-provider" => x with { ProposedProviderRef = "real:provider" },
            "unknown-path" => x with { Path = (SyntheticProviderPath)77 },
            "unknown-purpose" => x with { Purpose = (SyntheticProviderEvidencePurpose)77 },
            "default" => default,
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticProviderEvidenceResult.InvalidScope,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Fact]
    public void QualificationRequestCannotCarryUnverifiedPaymentInstructions()
    {
        var x = Input() with { ProposedPayeeRef = "test:payee" };
        Assert.Equal(SyntheticProviderEvidenceResult.InvalidScope,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Fact]
    public void QualificationRequestCannotPretendPaymentContractWasChecked()
    {
        var x = Input() with
        {
            DirectTaranomPayeeContractCurrent = CenterEvidence.Confirmed
        };
        Assert.Equal(SyntheticProviderEvidenceResult.InvalidScope,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Fact]
    public void FutureEvidenceVocabularyFailsClosed()
    {
        var x = Input() with { CurrentProviderQualityOversight = (CenterEvidence)88 };
        Assert.Equal(SyntheticProviderEvidenceResult.EvidenceMissing,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Fact]
    public void PaymentRecipientWithExplicitRejectedContractIsNeverAcceptable()
    {
        var x = Input(SyntheticProviderPath.DirectContract,
            SyntheticProviderEvidencePurpose.InspectProposedTreatmentCreditRecipient)
            with { DirectTaranomPayeeContractCurrent = CenterEvidence.Rejected };
        Assert.Equal(SyntheticProviderEvidenceResult.EvidenceRejected,
            SyntheticProviderNetworkEvidenceGate.Assess(x));
    }

    [Fact]
    public void NoResultMeansProviderWasActuallyEnabled()
    {
        string[] prohibited = ["Approved", "Activated", "Authorized",
            "Enrolled", "ReadyForService", "PaymentAllowed"];
        foreach (var name in Enum.GetNames<SyntheticProviderEvidenceResult>())
            Assert.DoesNotContain(prohibited, forbidden =>
                name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }
}
