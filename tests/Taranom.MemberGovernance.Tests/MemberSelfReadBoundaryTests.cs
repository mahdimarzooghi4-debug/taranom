using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class MemberSelfReadBoundaryTests
{
    private static SelfReadBoundaryInput Valid => new(
        SelfReadActorKind.Member,
        "test:memberA",
        "test:tenantA",
        "test:memberA",
        "test:tenantA",
        null,
        null,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        new ReadPrerequisiteSnapshot(
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed));

    [Fact]
    public void DefaultInputCannotClaimValidAccess()
    {
        Assert.Equal(SelfReadBoundaryResult.ActorDenied,
            MemberSelfReadBoundary.Assess(default));
    }

    [Fact]
    public void AllEvidenceConfirmedMeansOnlyFurtherReview()
    {
        Assert.Equal(SelfReadBoundaryResult.PreconditionsPresentOnly,
            MemberSelfReadBoundary.Assess(Valid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(999)]
    public void OtherPartyCannotInheritMemberSelfRead(int actorKind)
    {
        Assert.Equal(SelfReadBoundaryResult.ActorDenied,
            MemberSelfReadBoundary.Assess(Valid with {
                ActorKind = (SelfReadActorKind)actorKind }));
    }

    [Fact]
    public void SponsorUsingExactMemberAndTenantStillDenied()
    {
        var input = Valid with { ActorKind = SelfReadActorKind.Sponsor,
            BrowserMemberHint = "test:memberA", BrowserTenantHint = "test:tenantA" };
        Assert.Equal(SelfReadBoundaryResult.ActorDenied,
            MemberSelfReadBoundary.Assess(input));
    }

    [Fact]
    public void DifferentMemberWithinSameTenantDenied()
    {
        Assert.Equal(SelfReadBoundaryResult.ScopeDenied,
            MemberSelfReadBoundary.Assess(Valid with {
                ResourceOwnerMemberRef = "test:memberB" }));
    }

    [Fact]
    public void SameMemberIdentifierInAnotherTenantDenied()
    {
        Assert.Equal(SelfReadBoundaryResult.ScopeDenied,
            MemberSelfReadBoundary.Assess(Valid with {
                ResourceOwnerTenantRef = "test:tenantB" }));
    }

    [Fact]
    public void BrowserMemberHintCannotImpersonate()
    {
        Assert.Equal(SelfReadBoundaryResult.ScopeDenied,
            MemberSelfReadBoundary.Assess(Valid with {
                BrowserMemberHint = "test:memberB" }));
    }

    [Fact]
    public void BrowserTenantHintCannotChangeScope()
    {
        Assert.Equal(SelfReadBoundaryResult.ScopeDenied,
            MemberSelfReadBoundary.Assess(Valid with {
                BrowserTenantHint = "test:tenantB" }));
    }

    [Fact]
    public void MatchingBrowserHintsAreNotAnAuthorizationGrant()
    {
        Assert.Equal(SelfReadBoundaryResult.PreconditionsPresentOnly,
            MemberSelfReadBoundary.Assess(Valid with {
                BrowserMemberHint = "test:memberA",
                BrowserTenantHint = "test:tenantA" }));
        Assert.Equal(SelfReadBoundaryResult.EvidenceMissing,
            MemberSelfReadBoundary.Assess(Valid with {
                BrowserMemberHint = "test:memberA",
                CurrentSessionNotRevoked = EvidenceCheck.Missing }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("actual-identity")]
    [InlineData("test:member with space")]
    [InlineData("test:member/../another")]
    [InlineData("test:")]
    public void MissingOrUnsafeRefsFailClosed(string? candidate)
    {
        Assert.Equal(SelfReadBoundaryResult.EvidenceMissing,
            MemberSelfReadBoundary.Assess(Valid with {
                ServerResolvedMemberRef = candidate }));
    }

    [Fact]
    public void BoundedSyntheticRefsMayContainHyphenAndUnderscore()
    {
        var input = Valid with
        {
            ServerResolvedMemberRef = "test:member-a_1",
            ResourceOwnerMemberRef = "test:member-a_1",
            ServerResolvedTenantRef = "test:tenant-1",
            ResourceOwnerTenantRef = "test:tenant-1"
        };
        Assert.Equal(SelfReadBoundaryResult.PreconditionsPresentOnly,
            MemberSelfReadBoundary.Assess(input));
    }

    [Fact]
    public void ResourceRefWithDifferentCaseIsNotEqual()
    {
        Assert.Equal(SelfReadBoundaryResult.ScopeDenied,
            MemberSelfReadBoundary.Assess(Valid with {
                ResourceOwnerMemberRef = "test:MemberA" }));
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing)]
    [InlineData(EvidenceCheck.Rejected)]
    [InlineData((EvidenceCheck)345)]
    public void UnverifiedOrRevokedSessionNeverProgresses(EvidenceCheck status)
    {
        Assert.NotEqual(SelfReadBoundaryResult.PreconditionsPresentOnly,
            MemberSelfReadBoundary.Assess(Valid with {
                CurrentSessionNotRevoked = status }));
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing)]
    [InlineData(EvidenceCheck.Rejected)]
    [InlineData((EvidenceCheck)345)]
    public void MissingOrStaleMemberLinkNeverProgresses(EvidenceCheck status)
    {
        Assert.NotEqual(SelfReadBoundaryResult.PreconditionsPresentOnly,
            MemberSelfReadBoundary.Assess(Valid with {
                CurrentIdentityMemberLink = status }));
    }

    [Theory]
    [InlineData(EvidenceCheck.Missing)]
    [InlineData(EvidenceCheck.Rejected)]
    [InlineData((EvidenceCheck)345)]
    public void MissingOrStalePermissionVersionNeverProgresses(EvidenceCheck status)
    {
        Assert.NotEqual(SelfReadBoundaryResult.PreconditionsPresentOnly,
            MemberSelfReadBoundary.Assess(Valid with {
                CurrentPermissionVersion = status }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AnyRequiredOperationEvidenceMissingPreventsProgress(int index)
    {
        var facts = Valid.OtherReadPrerequisites;
        facts = index switch
        {
            0 => facts with { AuthenticatedServerSession = EvidenceCheck.Missing },
            1 => facts with { VerifiedMemberSubject = EvidenceCheck.Missing },
            2 => facts with { TenantAndResourcePermission = EvidenceCheck.Missing },
            3 => facts with { OperationSpecificLawfulBasis = EvidenceCheck.Missing },
            4 => facts with { AuditableDecisionPath = EvidenceCheck.Missing },
            5 => facts with { AuthoritativeSource = EvidenceCheck.Missing },
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        Assert.Equal(SelfReadBoundaryResult.EvidenceMissing,
            MemberSelfReadBoundary.Assess(Valid with {
                OtherReadPrerequisites = facts }));
    }

    [Fact]
    public void MissingResearchPurposeDoesNotInheritCarePermission()
    {
        Assert.Equal(SelfReadBoundaryResult.PreconditionsRejected,
            MemberSelfReadBoundary.Assess(Valid with {
                OtherReadPrerequisites = Valid.OtherReadPrerequisites with {
                    OperationSpecificLawfulBasis = EvidenceCheck.Rejected } }));
    }

    [Fact]
    public void AuditFailurePreventsProgressEvenIfAllOtherEvidenceConfirmed()
    {
        Assert.Equal(SelfReadBoundaryResult.EvidenceMissing,
            MemberSelfReadBoundary.Assess(Valid with {
                OtherReadPrerequisites = Valid.OtherReadPrerequisites with {
                    AuditableDecisionPath = EvidenceCheck.Missing } }));
    }

    [Fact]
    public void ExplicitRevocationTakesPrecedenceOverMissingSecondaryEvidence()
    {
        Assert.Equal(SelfReadBoundaryResult.PreconditionsRejected,
            MemberSelfReadBoundary.Assess(Valid with {
                CurrentSessionNotRevoked = EvidenceCheck.Rejected,
                CurrentPermissionVersion = EvidenceCheck.Missing }));
    }
}
