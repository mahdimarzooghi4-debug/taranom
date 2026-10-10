using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticSecondaryUseEvidenceGateTests
{
    private static SyntheticSecondaryUseEvidence AllClaimed() =>
        new(
            "test:member-one", "test:member-one", "test:member-one",
            "test:tenant-one", "test:research-purpose-v1",
            "test:health-slice-one", 3,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed);

    [Fact]
    public void CompleteCallerAssertionsNeverGrantDataOrDataset()
    {
        Assert.Equal(SecondaryUseReadinessResult.EvidencePresentOnly,
            SyntheticSecondaryUseEvidenceGate.Assess(AllClaimed()));
        Assert.DoesNotContain(Enum.GetNames<SecondaryUseReadinessResult>(),
            name => name.Contains("Approved") || name.Contains("Authorized") ||
                    name.Contains("Accessible") || name.Contains("ReadyForTraining") ||
                    name.Contains("Permitted"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EveryIndependentEvidenceDimensionIsRequired(int dimension)
    {
        var baseline = AllClaimed();
        var x = dimension switch
        {
            0 => baseline with { CurrentIndependentResearchConsent = EvidenceCheck.Missing },
            1 => baseline with { ExactPurposeAndDataScope = EvidenceCheck.Missing },
            2 => baseline with { CurrentMemberIdentityAndConsentLink = EvidenceCheck.Missing },
            3 => baseline with { SeparateLawfulProcessingBasis = EvidenceCheck.Missing },
            4 => baseline with { EffectiveDataProtection = EvidenceCheck.Missing },
            5 => baseline with { CurrentWithdrawalAndContinuityReview = EvidenceCheck.Missing },
            6 => baseline with { AuditableHumanGovernance = EvidenceCheck.Missing },
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        Assert.Equal(SecondaryUseReadinessResult.EvidenceMissing,
            SyntheticSecondaryUseEvidenceGate.Assess(x));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ExplicitRejectionOfAnyGateFailsClosed(int dimension)
    {
        var baseline = AllClaimed();
        var x = dimension switch
        {
            0 => baseline with { CurrentIndependentResearchConsent = EvidenceCheck.Rejected },
            1 => baseline with { ExactPurposeAndDataScope = EvidenceCheck.Rejected },
            2 => baseline with { CurrentMemberIdentityAndConsentLink = EvidenceCheck.Rejected },
            3 => baseline with { SeparateLawfulProcessingBasis = EvidenceCheck.Rejected },
            4 => baseline with { EffectiveDataProtection = EvidenceCheck.Rejected },
            5 => baseline with { CurrentWithdrawalAndContinuityReview = EvidenceCheck.Rejected },
            6 => baseline with { AuditableHumanGovernance = EvidenceCheck.Rejected },
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        Assert.Equal(SecondaryUseReadinessResult.ExplicitlyRejected,
            SyntheticSecondaryUseEvidenceGate.Assess(x));
    }

    [Fact]
    public void RejectedEvidenceDominatesMissingConsent()
    {
        var x = AllClaimed() with
        {
            CurrentIndependentResearchConsent = EvidenceCheck.Missing,
            EffectiveDataProtection = EvidenceCheck.Rejected
        };
        Assert.Equal(SecondaryUseReadinessResult.ExplicitlyRejected,
            SyntheticSecondaryUseEvidenceGate.Assess(x));
    }

    [Fact]
    public void InvalidOrFutureEnumValueCannotPassAsConfirmed()
    {
        var x = AllClaimed() with { SeparateLawfulProcessingBasis = (EvidenceCheck)99 };
        Assert.Equal(SecondaryUseReadinessResult.EvidenceMissing,
            SyntheticSecondaryUseEvidenceGate.Assess(x));
    }

    [Theory]
    [InlineData("real-member")]
    [InlineData("other-owner")]
    [InlineData("other-consent-member")]
    [InlineData("untrusted-tenant")]
    [InlineData("untrusted-purpose")]
    [InlineData("untrusted-data")]
    [InlineData("no-consent-version")]
    [InlineData("negative-consent-version")]
    [InlineData("empty-member")]
    public void InvalidOrMismatchedProvenanceCannotBecomeReady(string reason)
    {
        var current = AllClaimed();
        var x = reason switch
        {
            "real-member" => current with { ProposedMemberRef = "real:member" },
            "other-owner" => current with { DataOwnerMemberRef = "test:other-member" },
            "other-consent-member" => current with { ConsentSubjectMemberRef = "test:other-member" },
            "untrusted-tenant" => current with { TenantRef = "real:tenant" },
            "untrusted-purpose" => current with { PurposeScopeRef = "unscoped" },
            "untrusted-data" => current with { DataCategoryRef = "clinical-free-text" },
            "no-consent-version" => current with { IndependentConsentRevision = null },
            "negative-consent-version" => current with { IndependentConsentRevision = -1 },
            "empty-member" => current with { ProposedMemberRef = "" },
            _ => throw new ArgumentException("Unknown test case.")
        };
        Assert.Equal(SecondaryUseReadinessResult.InvalidScope,
            SyntheticSecondaryUseEvidenceGate.Assess(x));
    }

    [Fact]
    public void ExactPurposeAndDatasetScopeCanNeverBeImpliedByCareConsent()
    {
        // A presumed care delivery consent/medical record-sharing grant is
        // not a property of this boundary and cannot fill its research gate.
        var x = AllClaimed() with
        {
            CurrentIndependentResearchConsent = EvidenceCheck.Missing
        };
        Assert.Equal(SecondaryUseReadinessResult.EvidenceMissing,
            SyntheticSecondaryUseEvidenceGate.Assess(x));
        Assert.DoesNotContain(typeof(SyntheticSecondaryUseEvidence).GetProperties(),
            p => p.Name.Contains("CareDeliveryConsent") ||
                 p.Name.Contains("MembershipFee") ||
                 p.Name.Contains("EmployerApproval"));
    }

    [Fact]
    public void ChangingScopeRequiresSeparateEvidenceNotReusingOldPurpose()
    {
        var x = AllClaimed() with
        {
            PurposeScopeRef = "test:other-unreviewed-purpose",
            ExactPurposeAndDataScope = EvidenceCheck.Missing
        };
        Assert.Equal(SecondaryUseReadinessResult.EvidenceMissing,
            SyntheticSecondaryUseEvidenceGate.Assess(x));
    }

    [Fact]
    public void ExplicitUnknownWithdrawalImpactRemainsUnapproved()
    {
        // Real revocation/withdrawal semantics and derived dataset behavior
        // remain legal decisions; missing current review is NOT consent.
        var x = AllClaimed() with
        {
            CurrentWithdrawalAndContinuityReview = EvidenceCheck.Missing
        };
        Assert.Equal(SecondaryUseReadinessResult.EvidenceMissing,
            SyntheticSecondaryUseEvidenceGate.Assess(x));
    }

    [Fact]
    public void DefaultUninitializedValueIsInvalid()
    {
        Assert.Equal(SecondaryUseReadinessResult.InvalidScope,
            SyntheticSecondaryUseEvidenceGate.Assess(default));
    }
}
