using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class SyntheticAiDatasetBoundaryTests
{
    private const string Tenant = "test:tenant-a";
    private const string Purpose = "test:secondary-ai-research";
    private const string Category = "test:permitted-data-category";
    private static readonly string DigestA = new('A', 64);
    private static readonly string DigestB = new('B', 64);

    private static SyntheticSecondaryUseEvidence Consent(string member) =>
        new(member, member, member, Tenant, Purpose, Category, 1,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed);

    private static SyntheticAiDataSourceClaim Source(
        SyntheticAiDataPartition part, string source, string member,
        string? digest = null) =>
        new(part, source, 1, digest ?? DigestA, Consent(member),
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed, EvidenceCheck.Confirmed);

    private static SyntheticAiManifestClaim Valid => new(
        Tenant, "test:dataset-version-a", Purpose, Category,
        "test:training-policy-version", 1, DigestB,
        EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        [
            Source(SyntheticAiDataPartition.Training,
                "test:train-document-a", "test:member-a"),
            Source(SyntheticAiDataPartition.IndependentEvaluation,
                "test:eval-document-b", "test:member-b")
        ]);

    private static SyntheticAiManifestInspection Inspect(
        SyntheticAiManifestClaim? input = null) =>
        SyntheticAiDatasetBoundary.Inspect(input ?? Valid);

    [Fact]
    public void AllCallerClaimsProduceOnlyReviewEvidenceNeverDatasetOrTraining()
    {
        var result = Inspect();
        Assert.Equal(SyntheticAiDatasetInspection.ClaimedPreconditionsPresentOnly,
            result.Result);
        Assert.Equal(1, result.TrainingSourceCount);
        Assert.Equal(1, result.EvaluationSourceCount);
        Assert.Equal(64, result.ClaimedEvidenceFingerprint!.Length);
        Assert.DoesNotContain(Enum.GetNames<SyntheticAiDatasetInspection>(),
            name => name.Contains("Authorized") || name.Contains("TrainingStarted") ||
                    name.Contains("Approved") || name.Contains("Deploy"));
    }

    [Fact]
    public void DefaultInvalidManifestNeverBecomesAnEmptyApprovedDataset()
    {
        Assert.Equal(SyntheticAiDatasetInspection.InvalidManifest,
            SyntheticAiDatasetBoundary.Inspect(default).Result);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("dataset")]
    [InlineData("purpose")]
    [InlineData("category")]
    [InlineData("policy")]
    [InlineData("policy-revision")]
    [InlineData("manifest-hash")]
    [InlineData("unknown-manifest-status")]
    [InlineData("null-sources")]
    public void MalformedManifestFailsWithoutFingerprint(string mutation)
    {
        var v = Valid;
        v = mutation switch
        {
            "tenant" => v with { TenantRef = "tenant:real" },
            "dataset" => v with { DatasetVersionRef = "" },
            "purpose" => v with { ResearchPurposeRef = "test:bad purpose" },
            "category" => v with { DataCategoryRef = null },
            "policy" => v with { ExplicitTrainingPolicyRef = "real:policy" },
            "policy-revision" => v with { ExplicitTrainingPolicyRevision = 0 },
            "manifest-hash" => v with { ClaimedManifestDigestSha256 = "bad-digest" },
            "unknown-manifest-status" => v with {
                TrainingPolicyExplicitlyConfigured = (EvidenceCheck)77 },
            "null-sources" => v with { ClaimedSources = null },
            _ => throw new ArgumentException()
        };
        var r = Inspect(v);
        Assert.Equal(SyntheticAiDatasetInspection.InvalidManifest, r.Result);
        Assert.Null(r.ClaimedEvidenceFingerprint);
        Assert.Equal(0, r.TrainingSourceCount);
    }

    [Theory]
    [InlineData("source-ref")]
    [InlineData("source-revision")]
    [InlineData("source-hash")]
    [InlineData("bad-partition")]
    [InlineData("unknown-source-rights")]
    public void InvalidSourceFailsClosed(string mutation)
    {
        var source = Valid.ClaimedSources![0];
        source = mutation switch
        {
            "source-ref" => source with { SourceRef = "real:patient" },
            "source-revision" => source with { SourceRevision = -1 },
            "source-hash" => source with { ClaimedSourceDigestSha256 = "deadbeef" },
            "bad-partition" => source with { Partition = (SyntheticAiDataPartition)100 },
            "unknown-source-rights" => source with {
                CurrentSourceRights = (EvidenceCheck)99 },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticAiDatasetInspection.InvalidSource,
            Inspect(Valid with { ClaimedSources = [source, Valid.ClaimedSources[1]] }).Result);
    }

    [Theory]
    [InlineData("consent-tenant")]
    [InlineData("consent-purpose")]
    [InlineData("consent-category")]
    [InlineData("consent-member")]
    [InlineData("consent-subject")]
    [InlineData("consent-revision")]
    public void IndependentConsentMustBindExactScopeAndMember(string mutation)
    {
        var source = Valid.ClaimedSources![0];
        var c = source.ConsentEvidence;
        c = mutation switch
        {
            "consent-tenant" => c with { TenantRef = "test:tenant-b" },
            "consent-purpose" => c with { PurposeScopeRef = "test:care-purpose" },
            "consent-category" => c with { DataCategoryRef = "test:all-data" },
            "consent-member" => c with { DataOwnerMemberRef = "test:member-b" },
            "consent-subject" => c with { ConsentSubjectMemberRef = "test:member-b" },
            "consent-revision" => c with { IndependentConsentRevision = 0 },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticAiDatasetInspection.ScopeMismatch,
            Inspect(Valid with {
                ClaimedSources = [source with { ConsentEvidence = c },
                    Valid.ClaimedSources[1]]
            }).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EveryIndependentConsentOrLegalGateMustBePresent(int position)
    {
        var source = Valid.ClaimedSources![0];
        var c = SetConsentCheck(source.ConsentEvidence, position, EvidenceCheck.Missing);
        Assert.Equal(SyntheticAiDatasetInspection.ConsentOrLawfulBasisMissing,
            Inspect(Valid with {
                ClaimedSources = [source with { ConsentEvidence = c },
                    Valid.ClaimedSources[1]]
            }).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RejectionOfAnyConsentGateBlocksBothPartitions(int position)
    {
        var source = Valid.ClaimedSources![1];
        var c = SetConsentCheck(source.ConsentEvidence, position, EvidenceCheck.Rejected);
        Assert.Equal(SyntheticAiDatasetInspection.ConsentOrLawfulBasisRejected,
            Inspect(Valid with {
                ClaimedSources = [Valid.ClaimedSources[0],
                    source with { ConsentEvidence = c }]
            }).Result);
    }

    [Fact]
    public void UnknownConsentFlagMustNeverBeAssumedGranted()
    {
        var source = Valid.ClaimedSources![0];
        var c = source.ConsentEvidence with {
            CurrentIndependentResearchConsent = (EvidenceCheck)99 };
        Assert.Equal(SyntheticAiDatasetInspection.ConsentOrLawfulBasisMissing,
            Inspect(Valid with {
                ClaimedSources = [source with { ConsentEvidence = c },
                    Valid.ClaimedSources[1]]
            }).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachSourceRightOrIntegrityCheckMustBeIndependentlyPresent(int index)
    {
        var source = SetSourceCheck(Valid.ClaimedSources![1], index, EvidenceCheck.Missing);
        Assert.Equal(SyntheticAiDatasetInspection.SourceOrPolicyMissing,
            Inspect(Valid with {
                ClaimedSources = [Valid.ClaimedSources[0], source]
            }).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachSourceRejectionFailsClosed(int index)
    {
        var source = SetSourceCheck(Valid.ClaimedSources![0], index, EvidenceCheck.Rejected);
        Assert.Equal(SyntheticAiDatasetInspection.SourceOrPolicyRejected,
            Inspect(Valid with {
                ClaimedSources = [source, Valid.ClaimedSources[1]]
            }).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnconfiguredTrainingPolicyOrHumanGovernanceBlocksClaimedEligibility(int index)
    {
        var v = Valid;
        v = index switch
        {
            0 => v with { TrainingPolicyExplicitlyConfigured = EvidenceCheck.Missing },
            1 => v with { IndependentEvaluationGovernance = EvidenceCheck.Missing },
            2 => v with { HumanDataGovernanceReview = EvidenceCheck.Missing },
            _ => throw new ArgumentException()
        };
        Assert.Equal(SyntheticAiDatasetInspection.SourceOrPolicyMissing, Inspect(v).Result);
    }

    [Fact]
    public void ARejectedTrainingPolicyCannotBeOverriddenByDatasetDigest()
    {
        Assert.Equal(SyntheticAiDatasetInspection.SourceOrPolicyRejected,
            Inspect(Valid with {
                TrainingPolicyExplicitlyConfigured = EvidenceCheck.Rejected
            }).Result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DatasetRequiresBothDistinctPartitionsAsStructuralInput(bool training)
    {
        var only = Valid.ClaimedSources!.Where(x =>
            (x.Partition == SyntheticAiDataPartition.Training) == training).ToArray();
        Assert.Equal(SyntheticAiDatasetInspection.PartitionEvidenceMissing,
            Inspect(Valid with { ClaimedSources = only }).Result);
    }

    [Fact]
    public void EmptySourcesNeverInventEligibleDataset()
    {
        Assert.Equal(SyntheticAiDatasetInspection.PartitionEvidenceMissing,
            Inspect(Valid with {
                ClaimedSources = Array.Empty<SyntheticAiDataSourceClaim>()
            }).Result);
    }

    [Theory]
    [InlineData("same-source")]
    [InlineData("same-source-new-version")]
    public void ReusingSourceInTrainingAndEvaluationIsDenied(string mutation)
    {
        var train = Valid.ClaimedSources![0];
        var evaluation = Valid.ClaimedSources![1] with {
            SourceRef = train.SourceRef,
            SourceRevision = mutation == "same-source-new-version" ? 2 : 1
        };
        Assert.Equal(SyntheticAiDatasetInspection.DuplicateOrOverlappingSource,
            Inspect(Valid with { ClaimedSources = [train, evaluation] }).Result);
    }

    [Fact]
    public void DifferentSourceRefsAreNotClaimsOfStatisticalMemberIndependence()
    {
        var evaluation = Valid.ClaimedSources![1] with {
            ConsentEvidence = Consent("test:member-a")
        };
        Assert.Equal(SyntheticAiDatasetInspection.ClaimedPreconditionsPresentOnly,
            Inspect(Valid with {
                ClaimedSources = [Valid.ClaimedSources[0], evaluation]
            }).Result);
        // This boundary makes NO decision that member-disjoint evaluation
        // is scientifically valid. Independent validation remains external.
    }

    [Fact]
    public void CanonicalFingerprintIgnoresEntryOrderButNotConsentWithdrawal()
    {
        var v = Valid;
        var a = Inspect(v);
        Assert.Equal(a.ClaimedEvidenceFingerprint,
            Inspect(v with { ClaimedSources = v.ClaimedSources!.Reverse().ToArray() })
                .ClaimedEvidenceFingerprint);
        var s = v.ClaimedSources![0];
        var changed = s with { SourceRevision = 2 };
        Assert.NotEqual(a.ClaimedEvidenceFingerprint,
            Inspect(v with { ClaimedSources = [changed, v.ClaimedSources[1]] })
                .ClaimedEvidenceFingerprint);
        Assert.NotEqual(a.ClaimedEvidenceFingerprint,
            Inspect(v with { ExplicitTrainingPolicyRevision = 2 }).ClaimedEvidenceFingerprint);
    }

    [Fact]
    public void NoTrainingRuntimeArtifactsTokensOrRawMedicalFieldsExist()
    {
        var fields = typeof(SyntheticAiManifestClaim).GetProperties()
            .Select(p => p.Name).Concat(
                typeof(SyntheticAiDataSourceClaim).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(fields, s => new[] {
            "PatientRecord", "Diagnosis", "RawContent", "ModelWeights",
            "InferenceEndpoint", "TrainingRunId", "ProductionModel"
        }.Any(banned => s.Contains(banned, StringComparison.OrdinalIgnoreCase)));
    }

    private static SyntheticSecondaryUseEvidence SetConsentCheck(
        SyntheticSecondaryUseEvidence c, int index, EvidenceCheck v) => index switch
        {
            0 => c with { CurrentIndependentResearchConsent = v },
            1 => c with { ExactPurposeAndDataScope = v },
            2 => c with { CurrentMemberIdentityAndConsentLink = v },
            3 => c with { SeparateLawfulProcessingBasis = v },
            4 => c with { EffectiveDataProtection = v },
            5 => c with { CurrentWithdrawalAndContinuityReview = v },
            6 => c with { AuditableHumanGovernance = v },
            _ => throw new ArgumentException()
        };

    private static SyntheticAiDataSourceClaim SetSourceCheck(
        SyntheticAiDataSourceClaim s, int index, EvidenceCheck v) => index switch
        {
            0 => s with { CurrentSourceRights = v },
            1 => s with { CurrentSourceIntegrity = v },
            2 => s with { CurrentSourceQualityReview = v },
            3 => s with { IndependentPartitionLineage = v },
            _ => throw new ArgumentException()
        };
}
