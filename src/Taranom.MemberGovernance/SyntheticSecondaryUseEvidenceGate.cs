namespace Taranom.MemberGovernance;

// Pure, INTERNAL and SYNTHETIC evidence assessment for TRN-BIZ-035/036.
// This does not issue a data read, grant, token, dataset, training approval,
// processing right or clinical permission. Claimed checks are never trusted.
internal enum SecondaryUseReadinessResult
{
    InvalidScope = 1,
    ExplicitlyRejected = 2,
    EvidenceMissing = 3,
    EvidencePresentOnly = 4
}

internal readonly record struct SyntheticSecondaryUseEvidence(
    string? ProposedMemberRef,
    string? DataOwnerMemberRef,
    string? ConsentSubjectMemberRef,
    string? TenantRef,
    string? PurposeScopeRef,
    string? DataCategoryRef,
    long? IndependentConsentRevision,
    EvidenceCheck CurrentIndependentResearchConsent,
    EvidenceCheck ExactPurposeAndDataScope,
    EvidenceCheck CurrentMemberIdentityAndConsentLink,
    EvidenceCheck SeparateLawfulProcessingBasis,
    EvidenceCheck EffectiveDataProtection,
    EvidenceCheck CurrentWithdrawalAndContinuityReview,
    EvidenceCheck AuditableHumanGovernance);

// Unlike existing SyntheticConsentStore, this boundary has no mutation.
// Test-only "Confirmed" flags supplied by a caller never prove provenance,
// so even a fully filled vector results only in EvidencePresentOnly.
internal static class SyntheticSecondaryUseEvidenceGate
{
    internal static SecondaryUseReadinessResult Assess(
        SyntheticSecondaryUseEvidence input)
    {
        if (!IsSynthetic(input.ProposedMemberRef) ||
            !IsSynthetic(input.DataOwnerMemberRef) ||
            !IsSynthetic(input.ConsentSubjectMemberRef) ||
            !IsSynthetic(input.TenantRef) ||
            !IsSynthetic(input.PurposeScopeRef) ||
            !IsSynthetic(input.DataCategoryRef) ||
            input.IndependentConsentRevision is not > 0 ||
            !string.Equals(input.ProposedMemberRef, input.DataOwnerMemberRef,
                StringComparison.Ordinal) ||
            !string.Equals(input.ProposedMemberRef, input.ConsentSubjectMemberRef,
                StringComparison.Ordinal))
            return SecondaryUseReadinessResult.InvalidScope;

        // This is a separate purpose from care delivery, member subscription,
        // payment and ordinary clinical sharing. Those consent categories
        // are deliberately NOT accepted as inputs here.
        var evidence = new[]
        {
            input.CurrentIndependentResearchConsent,
            input.ExactPurposeAndDataScope,
            input.CurrentMemberIdentityAndConsentLink,
            input.SeparateLawfulProcessingBasis,
            input.EffectiveDataProtection,
            input.CurrentWithdrawalAndContinuityReview,
            input.AuditableHumanGovernance
        };
        if (evidence.Any(x => x == EvidenceCheck.Rejected))
            return SecondaryUseReadinessResult.ExplicitlyRejected;
        if (evidence.Any(x => x != EvidenceCheck.Confirmed))
            return SecondaryUseReadinessResult.EvidenceMissing;

        // Even with complete *claimed* evidence, only a later lawfully
        // authorized human/server-side gate can decide actual data use.
        return SecondaryUseReadinessResult.EvidencePresentOnly;
    }

    private static bool IsSynthetic(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
