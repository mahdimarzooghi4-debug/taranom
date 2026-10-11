using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Taranom.MemberGovernance;

// TRN-BIZ-036/037/038/039: PURE TEST-ONLY review of a claimed AI dataset
// manifest. No dataset contents, model weights, training, network, authorizing
// consent grant, automatic candidate creation or Production operation.
internal enum SyntheticAiDataPartition
{
    Training = 1,
    IndependentEvaluation = 2
}

internal enum SyntheticAiDatasetInspection
{
    InvalidManifest = 1,
    InvalidSource = 2,
    ScopeMismatch = 3,
    DuplicateOrOverlappingSource = 4,
    PartitionEvidenceMissing = 5,
    ConsentOrLawfulBasisRejected = 6,
    ConsentOrLawfulBasisMissing = 7,
    SourceOrPolicyRejected = 8,
    SourceOrPolicyMissing = 9,
    ClaimedPreconditionsPresentOnly = 10
}

// No record payload or real provenance is accepted: all untrusted test labels.
// ConsentEvidence MUST be checked via the EXISTING independent secondary-use
// assessor, never a new substitute for identity/consent governance.
internal readonly record struct SyntheticAiDataSourceClaim(
    SyntheticAiDataPartition Partition,
    string? SourceRef,
    long SourceRevision,
    string? ClaimedSourceDigestSha256,
    SyntheticSecondaryUseEvidence ConsentEvidence,
    EvidenceCheck CurrentSourceRights,
    EvidenceCheck CurrentSourceIntegrity,
    EvidenceCheck CurrentSourceQualityReview,
    EvidenceCheck IndependentPartitionLineage);

internal readonly record struct SyntheticAiManifestClaim(
    string? TenantRef,
    string? DatasetVersionRef,
    string? ResearchPurposeRef,
    string? DataCategoryRef,
    string? ExplicitTrainingPolicyRef,
    long ExplicitTrainingPolicyRevision,
    string? ClaimedManifestDigestSha256,
    EvidenceCheck TrainingPolicyExplicitlyConfigured,
    EvidenceCheck IndependentEvaluationGovernance,
    EvidenceCheck HumanDataGovernanceReview,
    IReadOnlyList<SyntheticAiDataSourceClaim>? ClaimedSources);

// A checksum of supplied claims is not an evidence signature. A result with
// counts can NEVER certify a dataset or permit training/release.
internal readonly record struct SyntheticAiManifestInspection(
    SyntheticAiDatasetInspection Result,
    int TrainingSourceCount,
    int EvaluationSourceCount,
    string? ClaimedEvidenceFingerprint);

internal static class SyntheticAiDatasetBoundary
{
    internal static SyntheticAiManifestInspection Inspect(SyntheticAiManifestClaim manifest)
    {
        static SyntheticAiManifestInspection Fail(SyntheticAiDatasetInspection state) =>
            new(state, 0, 0, null);

        if (!TestRef(manifest.TenantRef) ||
            !TestRef(manifest.DatasetVersionRef) ||
            !TestRef(manifest.ResearchPurposeRef) ||
            !TestRef(manifest.DataCategoryRef) ||
            !TestRef(manifest.ExplicitTrainingPolicyRef) ||
            manifest.ExplicitTrainingPolicyRevision < 1 ||
            !Sha256(manifest.ClaimedManifestDigestSha256) ||
            !Defined(manifest.TrainingPolicyExplicitlyConfigured) ||
            !Defined(manifest.IndependentEvaluationGovernance) ||
            !Defined(manifest.HumanDataGovernanceReview) ||
            manifest.ClaimedSources is null)
            return Fail(SyntheticAiDatasetInspection.InvalidManifest);

        var seenSourceRefs = new HashSet<string>(StringComparer.Ordinal);
        var validated = new List<SyntheticAiDataSourceClaim>();
        var sourceStatuses = new List<EvidenceCheck>();
        var consentMissing = false;
        var consentRejected = false;
        foreach (var source in manifest.ClaimedSources)
        {
            if (!Enum.IsDefined(source.Partition) ||
                !TestRef(source.SourceRef) ||
                source.SourceRevision < 1 ||
                !Sha256(source.ClaimedSourceDigestSha256) ||
                !Defined(source.CurrentSourceRights) ||
                !Defined(source.CurrentSourceIntegrity) ||
                !Defined(source.CurrentSourceQualityReview) ||
                !Defined(source.IndependentPartitionLineage))
                return Fail(SyntheticAiDatasetInspection.InvalidSource);

            // A dataset may NEVER silently switch tenant, purpose, or data
            // category between independent per-member consent and manifest.
            if (source.ConsentEvidence.TenantRef != manifest.TenantRef ||
                source.ConsentEvidence.PurposeScopeRef != manifest.ResearchPurposeRef ||
                source.ConsentEvidence.DataCategoryRef != manifest.DataCategoryRef)
                return Fail(SyntheticAiDatasetInspection.ScopeMismatch);

            var consent = SyntheticSecondaryUseEvidenceGate.Assess(source.ConsentEvidence);
            if (consent == SecondaryUseReadinessResult.InvalidScope)
                return Fail(SyntheticAiDatasetInspection.ScopeMismatch);

            if (!seenSourceRefs.Add(source.SourceRef!))
                return Fail(SyntheticAiDatasetInspection.DuplicateOrOverlappingSource);

            consentRejected |= consent == SecondaryUseReadinessResult.ExplicitlyRejected;
            consentMissing |= consent == SecondaryUseReadinessResult.EvidenceMissing;
            sourceStatuses.AddRange([
                source.CurrentSourceRights,
                source.CurrentSourceIntegrity,
                source.CurrentSourceQualityReview,
                source.IndependentPartitionLineage
            ]);
            validated.Add(source);
        }

        // Both sets are structural prerequisites, NOT sample-size or
        // statistical adequacy rules. No numeric model/quality threshold.
        var trainingCount = validated.Count(x =>
            x.Partition == SyntheticAiDataPartition.Training);
        var evaluationCount = validated.Count(x =>
            x.Partition == SyntheticAiDataPartition.IndependentEvaluation);
        if (trainingCount == 0 || evaluationCount == 0)
            return Fail(SyntheticAiDatasetInspection.PartitionEvidenceMissing);
        if (consentRejected)
            return Fail(SyntheticAiDatasetInspection.ConsentOrLawfulBasisRejected);
        if (consentMissing)
            return Fail(SyntheticAiDatasetInspection.ConsentOrLawfulBasisMissing);

        var policyChecks = new[] {
            manifest.TrainingPolicyExplicitlyConfigured,
            manifest.IndependentEvaluationGovernance,
            manifest.HumanDataGovernanceReview
        };
        if (policyChecks.Concat(sourceStatuses).Any(x => x == EvidenceCheck.Rejected))
            return Fail(SyntheticAiDatasetInspection.SourceOrPolicyRejected);
        if (policyChecks.Concat(sourceStatuses).Any(x => x != EvidenceCheck.Confirmed))
            return Fail(SyntheticAiDatasetInspection.SourceOrPolicyMissing);

        // This is the exact textual identity of *claimed* inputs, not actual
        // data, consent validity, digest attestation or partition proof.
        var buffer = new StringBuilder("taranom.synthetic-ai-manifest.v1");
        void Add(string v) => buffer.Append('|')
            .Append(v.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(v);
        void Number(long v) => Add(v.ToString(CultureInfo.InvariantCulture));
        void Check(EvidenceCheck v) => Number((int)v);
        Add(manifest.TenantRef!);
        Add(manifest.DatasetVersionRef!);
        Add(manifest.ResearchPurposeRef!);
        Add(manifest.DataCategoryRef!);
        Add(manifest.ExplicitTrainingPolicyRef!);
        Number(manifest.ExplicitTrainingPolicyRevision);
        Add(manifest.ClaimedManifestDigestSha256!);
        Check(manifest.TrainingPolicyExplicitlyConfigured);
        Check(manifest.IndependentEvaluationGovernance);
        Check(manifest.HumanDataGovernanceReview);
        foreach (var s in validated.OrderBy(x => x.SourceRef, StringComparer.Ordinal))
        {
            Number((int)s.Partition);
            Add(s.SourceRef!);
            Number(s.SourceRevision);
            Add(s.ClaimedSourceDigestSha256!);
            var c = s.ConsentEvidence;
            Add(c.ProposedMemberRef!);
            Add(c.DataOwnerMemberRef!);
            Add(c.ConsentSubjectMemberRef!);
            Add(c.TenantRef!);
            Add(c.PurposeScopeRef!);
            Add(c.DataCategoryRef!);
            Number(c.IndependentConsentRevision!.Value);
            Check(c.CurrentIndependentResearchConsent);
            Check(c.ExactPurposeAndDataScope);
            Check(c.CurrentMemberIdentityAndConsentLink);
            Check(c.SeparateLawfulProcessingBasis);
            Check(c.EffectiveDataProtection);
            Check(c.CurrentWithdrawalAndContinuityReview);
            Check(c.AuditableHumanGovernance);
            Check(s.CurrentSourceRights);
            Check(s.CurrentSourceIntegrity);
            Check(s.CurrentSourceQualityReview);
            Check(s.IndependentPartitionLineage);
        }
        return new(SyntheticAiDatasetInspection.ClaimedPreconditionsPresentOnly,
            trainingCount, evaluationCount,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString()))));
    }

    private static bool Defined(EvidenceCheck v) =>
        v is EvidenceCheck.Missing or EvidenceCheck.Confirmed or EvidenceCheck.Rejected;

    private static bool TestRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');

    private static bool Sha256(string? value) =>
        value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
