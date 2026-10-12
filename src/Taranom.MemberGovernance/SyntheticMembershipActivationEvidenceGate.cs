namespace Taranom.MemberGovernance;

// TRN-BIZ-016...024, -028/-029: INTERNAL, PURE, SYNTHETIC-ONLY
// composition of existing ProofingCaseJournal + SubscriptionPayerBoundary.
// NEVER an auto-activation decision, verified KYC/payment or waiting clock.
internal enum SyntheticActivationInspection
{
    InvalidInput = 1,
    ProofingHistoryInvalid = 2,
    StaleIdentityOrCaseRevision = 3,
    AlreadyActiveSeparateAuthority = 4,
    IdentityCorrectionOrReverificationRequired = 5,
    FundingScopeDenied = 6,
    ClaimedEvidenceRejected = 7,
    ClaimedEvidenceMissing = 8,
    ClaimedPrerequisitesPresentOnly = 9
}

internal readonly record struct SyntheticActivationEvidenceInput(
    string? MemberRef,
    string? TenantRef,
    string? UnverifiedProofingCaseRef,
    long ExpectedProofingCaseRevision,
    long ExpectedIdentityRevision,
    ProofingCaseSnapshot? ProofingHistory,
    EvidenceCheck CurrentOfficialActivationStatusKnown,
    bool ClaimedCurrentlyActivated,
    EvidenceCheck CurrentProofingBelongsToExactMember,
    EvidenceCheck CurrentProofingSourceLicensedAndContracted,
    EvidenceCheck IndependentMemberLegalProcessingBasis,
    EvidenceCheck ExplicitMemberTermsEvidence,
    EvidenceCheck CurrentAllOtherActivationConditionsReviewed,
    SyntheticSubscriptionPayerInput ClaimedSubscriptionFunding);

// Never an actual verified applicant, activated member, payment receipt
// or eligibility verdict. Revisions are for synthetic structural diagnostics.
internal readonly record struct SyntheticActivationEvidenceRead(
    SyntheticActivationInspection Result,
    long? ObservedProofingCaseRevision,
    long? ObservedIdentityRevision,
    bool OfficialActivationIssued,
    bool WaitingClockStarted);

// No identity payload, national ID, fee, bank transfer, account token, API,
// provider integration, database, activation event or credit/wallet mutation.
internal static class SyntheticMembershipActivationEvidenceGate
{
    internal static SyntheticActivationEvidenceRead Inspect(
        SyntheticActivationEvidenceInput input)
    {
        static SyntheticActivationEvidenceRead Deny(
            SyntheticActivationInspection result) => new(result, null, null, false, false);

        if (!Test(input.MemberRef) || !Test(input.TenantRef) ||
            !Test(input.UnverifiedProofingCaseRef) ||
            input.ExpectedProofingCaseRevision < 1 ||
            input.ExpectedIdentityRevision < 0 ||
            input.ProofingHistory is null ||
            !Defined(input.CurrentOfficialActivationStatusKnown) ||
            !Defined(input.CurrentProofingBelongsToExactMember) ||
            !Defined(input.CurrentProofingSourceLicensedAndContracted) ||
            !Defined(input.IndependentMemberLegalProcessingBasis) ||
            !Defined(input.ExplicitMemberTermsEvidence) ||
            !Defined(input.CurrentAllOtherActivationConditionsReviewed))
            return Deny(SyntheticActivationInspection.InvalidInput);

        // The official activation event is an independently authorized future
        // command; even a validated proofing journal MUST NOT create it.
        if (input.ClaimedCurrentlyActivated)
            return Deny(SyntheticActivationInspection.AlreadyActiveSeparateAuthority);

        var history = input.ProofingHistory;
        if (history.History.IsEmpty || history.CaseRevision != history.History.Length ||
            history.IdentityRevision < 0)
            return Deny(SyntheticActivationInspection.ProofingHistoryInvalid);

        // Caller-supplied evidence is rechecked against the EXISTING
        // versioned proofing journal, including corrections and stale evidence.
        // This establishes only structural consistency, not source authenticity.
        var replay = ProofingCaseSnapshot.Empty;
        foreach (var evt in history.History)
        {
            var applied = ProofingCaseJournal.Apply(replay, evt.Command);
            if (applied.Outcome != ProofingJournalOutcome.Applied ||
                applied.Snapshot.CaseRevision != evt.CaseRevision ||
                applied.Snapshot.IdentityRevision != evt.IdentityRevision ||
                applied.Snapshot.Review != evt.ResultingReview)
                return Deny(SyntheticActivationInspection.ProofingHistoryInvalid);
            replay = applied.Snapshot;
        }

        if (replay.CaseRevision != history.CaseRevision ||
            replay.IdentityRevision != history.IdentityRevision ||
            replay.Review != history.Review)
            return Deny(SyntheticActivationInspection.ProofingHistoryInvalid);

        if (input.ExpectedProofingCaseRevision != history.CaseRevision ||
            input.ExpectedIdentityRevision != history.IdentityRevision)
            return Deny(SyntheticActivationInspection.StaleIdentityOrCaseRevision);

        var last = history.History[^1];
        if (history.Review != ProofingReviewState.ValidatedProofingEvidenceOnly ||
            last.Command.Action != ProofingCaseAction.ObserveProofing ||
            last.IdentityRevision != history.IdentityRevision ||
            last.Command.ExpectedIdentityRevision != history.IdentityRevision ||
            last.Command.ProofingEvidence.Observation != ProofingObservation.VerifiedMatch)
            return Deny(SyntheticActivationInspection.IdentityCorrectionOrReverificationRequired);

        var funding = input.ClaimedSubscriptionFunding;
        if (funding.RequestedCapability !=
                SyntheticPayerRequestedCapability.InspectSubscriptionFundingEvidence ||
            funding.MemberRef != input.MemberRef ||
            funding.MemberTenantRef != input.TenantRef ||
            funding.IndividualWalletOwnerRef != input.MemberRef)
            return Deny(SyntheticActivationInspection.FundingScopeDenied);

        var payer = SyntheticSubscriptionPayerBoundary.Evaluate(funding);
        if (payer is SyntheticPayerBoundaryResult.InvalidInput or
            SyntheticPayerBoundaryResult.ContractScopeDenied or
            SyntheticPayerBoundaryResult.PayerPrivilegeDenied or
            SyntheticPayerBoundaryResult.SeparateAuthorizationRequired)
            return Deny(SyntheticActivationInspection.FundingScopeDenied);

        var others = new[]
        {
            input.CurrentOfficialActivationStatusKnown,
            input.CurrentProofingBelongsToExactMember,
            input.CurrentProofingSourceLicensedAndContracted,
            input.IndependentMemberLegalProcessingBasis,
            input.ExplicitMemberTermsEvidence,
            input.CurrentAllOtherActivationConditionsReviewed
        };

        if (payer == SyntheticPayerBoundaryResult.EvidenceRejected ||
            others.Any(x => x == EvidenceCheck.Rejected))
            return Deny(SyntheticActivationInspection.ClaimedEvidenceRejected);

        if (payer != SyntheticPayerBoundaryResult.FundingEvidencePresentOnly ||
            others.Any(x => x != EvidenceCheck.Confirmed))
            return Deny(SyntheticActivationInspection.ClaimedEvidenceMissing);

        // Nothing in an untrusted caller-supplied vector can confirm that all
        // actual activation preconditions are legally defined or fulfilled.
        return new(SyntheticActivationInspection.ClaimedPrerequisitesPresentOnly,
            history.CaseRevision, history.IdentityRevision, false, false);
    }

    private static bool Defined(EvidenceCheck value) =>
        value is EvidenceCheck.Missing or EvidenceCheck.Rejected or EvidenceCheck.Confirmed;

    private static bool Test(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
