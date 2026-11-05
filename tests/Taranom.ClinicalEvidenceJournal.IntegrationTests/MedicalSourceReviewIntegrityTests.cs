using Taranom.ClinicalEvidenceJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalEvidenceJournal.IntegrationTests;

public sealed class MedicalSourceReviewIntegrityTests
{
    private static MedicalSourceReviewCommand ValidCommand()=>new(
        Guid.NewGuid(),0,"test:source-1",new string('a',64),"RIGHTS","REQUEST_EVIDENCE",
        "reviewer:claimed-test","evidence:claimed-test",new string('b',64));

    [Fact]
    public void SharedContractAcceptsSyntheticClaimsWithoutTreatingThemAsVerifiedIdentityOrEvidence()
    {
        var command=ValidCommand();

        Assert.True(MedicalSourceReviewIntegrity.IsValidCommand(command));
        Assert.True(MedicalSourceReviewIntegrity.IsSourceRef(command.SourceRef));
        Assert.True(MedicalSourceReviewIntegrity.IsClaimRef(command.ReviewerRefClaimed,"reviewer:"));
        Assert.True(MedicalSourceReviewIntegrity.IsClaimRef(command.EvidenceRefClaimed,"evidence:"));
    }

    [Theory]
    [InlineData("LANE")]
    [InlineData("")]
    public void SharedContractRejectsUnknownReviewLanes(string lane)
    {
        Assert.False(MedicalSourceReviewIntegrity.IsValidCommand(ValidCommand() with { Lane=lane }));
    }

    [Theory]
    [InlineData("REQUEST_EVIDENCE")]
    [InlineData("RECOMMEND_ACCEPT")]
    [InlineData("RECOMMEND_REJECT")]
    [InlineData("ACCEPT")]
    public void SharedContractAcceptsOnlyDefinedNonAuthoritativeRecommendations(string recommendation)
    {
        var expected=recommendation is "REQUEST_EVIDENCE" or "RECOMMEND_ACCEPT" or "RECOMMEND_REJECT";

        Assert.Equal(expected,MedicalSourceReviewIntegrity.IsValidCommand(
            ValidCommand() with { Recommendation=recommendation }));
    }

    [Fact]
    public void SharedContractRejectsMalformedDigestsAndClaimReferences()
    {
        Assert.False(MedicalSourceReviewIntegrity.IsValidCommand(
            ValidCommand() with { EvidencePackageSha256=new string('A',64) }));
        Assert.False(MedicalSourceReviewIntegrity.IsValidCommand(
            ValidCommand() with { ReviewerRefClaimed="reviewer:" }));
        Assert.False(MedicalSourceReviewIntegrity.IsValidCommand(
            ValidCommand() with { EvidenceRefClaimed="evidence:bad value" }));
    }

    [Fact]
    public void EventDigestIsStableAndBoundToRevisionAndPreviousHash()
    {
        var previous=new string('c',64);
        var first=MedicalSourceReviewIntegrity.EventDigest(previous,1,"{}");

        Assert.Equal(first,MedicalSourceReviewIntegrity.EventDigest(previous,1,"{}"));
        Assert.NotEqual(first,MedicalSourceReviewIntegrity.EventDigest(previous,2,"{}"));
        Assert.NotEqual(first,MedicalSourceReviewIntegrity.EventDigest(new string('d',64),1,"{}"));
        Assert.Equal(64,first.Length);
    }
}
