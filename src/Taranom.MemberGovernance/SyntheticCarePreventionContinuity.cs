namespace Taranom.MemberGovernance;

// TRN-BIZ-046/047: INTERNAL TEST-ONLY READ. This joins two already defined
// untrusted source models for a strictly NON-OPERATIONAL continuity review.
// No real service, patient, provider, consent, clinician decision or notification.
internal enum SyntheticCarePreventionContinuityState
{
    InvalidInput = 1,
    UnverifiedCareRequestMissing = 2,
    CareHistoryInvalid = 3,
    StaleCareHistory = 4,
    MemberOrCaseScopeMismatch = 5,
    ParticipationScopeInvalid = 6,
    MemberDeclined = 7,
    ParticipationEvidenceRejected = 8,
    ParticipationEvidenceMissing = 9,
    IndependentConsentOrClinicianDecisionRequired = 10,
    UnverifiedClinicalSourceMissing = 11,
    ClaimedPrerequisitesPresentOnly = 12
}

internal readonly record struct SyntheticCarePreventionContinuityRead(
    SyntheticCarePreventionContinuityState State,
    SyntheticPreventionTrack? Track,
    SyntheticParticipationOrigin? Origin,
    SyntheticPreventionReviewResult? UntrustedParticipationInspection,
    long CareHistoryRevision,
    int UnverifiedCoordinationNoteCount,
    int UnverifiedClinicalReferenceCount,
    bool ParticipationActivated,
    bool EducationPublished,
    bool ClinicalCareStartedOrChanged,
    bool ResearchOrAiConsentGranted,
    bool MedicalRecordDisclosed,
    bool AutomatedFollowUpScheduled);

// A claimed test: correspondence does not establish authentic case-to-program
// linkage. All inputs are caller-controlled, including "Confirmed" flags.
// The existing owner reducers are replayed rather than copied or bypassed.
internal static class SyntheticCarePreventionContinuity
{
    internal static SyntheticCarePreventionContinuityRead Inspect(
        SyntheticCareTrailSnapshot? care,
        string? expectedCaseRef,
        string? expectedMemberRef,
        long expectedCareRevision,
        string? claimedCaseProgramCorrespondenceRef,
        SyntheticPreventionEvidence prevention)
    {
        static SyntheticCarePreventionContinuityRead Deny(
            SyntheticCarePreventionContinuityState state) =>
            new(state, null, null, null, 0, 0, 0,
                false, false, false, false, false, false);

        if (!Test(expectedCaseRef) || !Test(expectedMemberRef) ||
            !Test(claimedCaseProgramCorrespondenceRef) ||
            expectedCareRevision < 1)
            return Deny(SyntheticCarePreventionContinuityState.InvalidInput);
        if (care is null || care.History.IsDefaultOrEmpty)
            return Deny(SyntheticCarePreventionContinuityState.UnverifiedCareRequestMissing);

        var replay = SyntheticCareTrailSnapshot.Empty;
        foreach (var item in care.History)
        {
            var step = SyntheticCareCoordinationTrail.Apply(replay, item.Command);
            if (step.Outcome != SyntheticCareTrailOutcome.RecordedUnverified ||
                step.Snapshot.Revision != item.Revision)
                return Deny(SyntheticCarePreventionContinuityState.CareHistoryInvalid);
            replay = step.Snapshot;
        }
        if (replay.Revision != care.Revision)
            return Deny(SyntheticCarePreventionContinuityState.CareHistoryInvalid);
        if (expectedCareRevision != care.Revision)
            return Deny(SyntheticCarePreventionContinuityState.StaleCareHistory);

        var origin = replay.History[0].Command;
        if (origin.CaseRef != expectedCaseRef ||
            origin.MemberRef != expectedMemberRef ||
            prevention.MemberRef != expectedMemberRef ||
            prevention.MemberParticipationSubjectRef != expectedMemberRef)
            return Deny(SyntheticCarePreventionContinuityState.MemberOrCaseScopeMismatch);

        // The care trail has NO tenant, program or clinician foreign key.
        // These claimed references are not evidence of a real encounter.
        var inspection = SyntheticPreventionParticipationGate.Assess(prevention);
        if (inspection is SyntheticPreventionReviewResult.InvalidScope or
            SyntheticPreventionReviewResult.ProposerScopeDenied)
            return Deny(SyntheticCarePreventionContinuityState.ParticipationScopeInvalid);

        var contacts = replay.History.Count(x =>
            x.Command.Action == SyntheticCareTrailAction.RecordUnverifiedCoordinatorContact);
        var sources = replay.History.Count(x =>
            x.Command.Action == SyntheticCareTrailAction.AttachUnverifiedClinicalSourceReference);

        var status = inspection switch
        {
            SyntheticPreventionReviewResult.MemberDeclined =>
                SyntheticCarePreventionContinuityState.MemberDeclined,
            SyntheticPreventionReviewResult.EvidenceRejected =>
                SyntheticCarePreventionContinuityState.ParticipationEvidenceRejected,
            SyntheticPreventionReviewResult.EvidenceMissing =>
                SyntheticCarePreventionContinuityState.ParticipationEvidenceMissing,
            SyntheticPreventionReviewResult.DistinctConsentRequired or
            SyntheticPreventionReviewResult.ProhibitedAutomaticAction =>
                SyntheticCarePreventionContinuityState.IndependentConsentOrClinicianDecisionRequired,
            _ when prevention.Track == SyntheticPreventionTrack.LongitudinalClinicalCare &&
                sources == 0 =>
                SyntheticCarePreventionContinuityState.UnverifiedClinicalSourceMissing,
            _ => SyntheticCarePreventionContinuityState.ClaimedPrerequisitesPresentOnly
        };

        // No state, even an all-Confirmed set of claims, is authorization.
        return new(status, prevention.Track, prevention.Origin, inspection,
            replay.Revision, contacts, sources,
            false, false, false, false, false, false);
    }

    private static bool Test(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
