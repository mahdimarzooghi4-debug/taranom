using Taranom.ClinicalEvidenceIntake;
using IntakeDomain = global::Taranom.ClinicalEvidenceIntake.ClinicalEvidenceIntake;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

// Test-only candidate vocabulary. No legal reviewer identity, official
// physician registry, trust root, or operational authorization is present.
internal enum ReviewerProvenanceCheck
{
    Missing = 0,
    ConfirmedByTestDouble = 1,
    Denied = 2
}

internal enum ReviewerProvenanceOutcome
{
    InvalidScope = 1,
    AuthoritativeSourceUnavailable = 2,
    ReviewerIdentityUnverified = 3,
    GrantStaleOrRevoked = 4,
    ReviewerNotIndependent = 5,
    EvidenceOriginUnverified = 6,
    EvidenceMismatch = 7,
    ReviewNoteMissingOrCorrupt = 8,
    PreconditionsPresentOnly = 9
}

internal readonly record struct ReviewerProvenanceRequest(
    string CenterRef, string SubjectRef, EvidenceSubjectKind SubjectKind,
    long EvidenceRevision, Guid ReviewNoteRequestId);

// Every property is simulated in tests until a real, independently approved
// issuer/registry exists. Even all Confirmed values can NEVER grant access.
internal readonly record struct ReviewerProvenanceEvidence(
    string SessionSubjectRef, string ResolvedReviewerRef, string CenterRef,
    string PreviousGrantVersionRef, string CurrentGrantVersionRef,
    string EvidenceSourceRef,
    ReviewerProvenanceCheck AuthenticatedSession,
    ReviewerProvenanceCheck CurrentIdentityMapping,
    ReviewerProvenanceCheck CurrentReviewDelegation,
    ReviewerProvenanceCheck GrantNotRevoked,
    ReviewerProvenanceCheck IndependentOriginAttested,
    ReviewerProvenanceCheck AuditAvailable);

internal interface IReviewerProvenanceSource
{
    Task<ReviewerProvenanceEvidence?> ResolveFromServerAsync(
        Guid noteRequestId, CancellationToken cancellationToken);
}

// A review note stored in PostgreSQL is an untrusted observation. This
// inspector can ONLY confirm synthetic prerequisites, never review approval.
// Its default constructor has NO trusted reviewer source and fails closed.
internal sealed class PgReviewerProvenanceInterlock(
    PgHumanReviewNoteJournal notes, PgClinicalEvidenceJournal evidence,
    IReviewerProvenanceSource? authoritativeSource = null)
{
    internal async Task<ReviewerProvenanceOutcome> InspectAsync(
        ReviewerProvenanceRequest request, CancellationToken ct = default)
    {
        if (!IntakeDomain.IsSyntheticRef(request.CenterRef) ||
            !IntakeDomain.IsSyntheticRef(request.SubjectRef) ||
            !Enum.IsDefined(request.SubjectKind) ||
            request.EvidenceRevision is < 1 or > int.MaxValue ||
            request.ReviewNoteRequestId == Guid.Empty)
            return ReviewerProvenanceOutcome.InvalidScope;

        // No source: do NOT read private notes or accepted evidence.
        if (authoritativeSource is null)
            return ReviewerProvenanceOutcome.AuthoritativeSourceUnavailable;

        ReviewerProvenanceEvidence? evidenceFromSource;
        try
        {
            evidenceFromSource = await authoritativeSource.ResolveFromServerAsync(
                request.ReviewNoteRequestId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ReviewerProvenanceOutcome.AuthoritativeSourceUnavailable;
        }
        if (evidenceFromSource is not { } attested)
            return ReviewerProvenanceOutcome.AuthoritativeSourceUnavailable;

        if (!IntakeDomain.IsSyntheticRef(attested.SessionSubjectRef) ||
            !IntakeDomain.IsSyntheticRef(attested.ResolvedReviewerRef) ||
            !IntakeDomain.IsSyntheticRef(attested.CenterRef) ||
            !IntakeDomain.IsSyntheticRef(attested.PreviousGrantVersionRef) ||
            !IntakeDomain.IsSyntheticRef(attested.CurrentGrantVersionRef) ||
            !IntakeDomain.IsSyntheticRef(attested.EvidenceSourceRef) ||
            attested.AuthenticatedSession != ReviewerProvenanceCheck.ConfirmedByTestDouble ||
            attested.CurrentIdentityMapping != ReviewerProvenanceCheck.ConfirmedByTestDouble ||
            attested.SessionSubjectRef != attested.ResolvedReviewerRef)
            return ReviewerProvenanceOutcome.ReviewerIdentityUnverified;

        if (attested.CenterRef != request.CenterRef)
            return ReviewerProvenanceOutcome.EvidenceMismatch;

        if (attested.CurrentReviewDelegation != ReviewerProvenanceCheck.ConfirmedByTestDouble ||
            attested.GrantNotRevoked != ReviewerProvenanceCheck.ConfirmedByTestDouble ||
            attested.AuditAvailable != ReviewerProvenanceCheck.ConfirmedByTestDouble ||
            attested.PreviousGrantVersionRef != attested.CurrentGrantVersionRef)
            return ReviewerProvenanceOutcome.GrantStaleOrRevoked;

        if (attested.IndependentOriginAttested !=
            ReviewerProvenanceCheck.ConfirmedByTestDouble)
            return ReviewerProvenanceOutcome.EvidenceOriginUnverified;

        IReadOnlyList<HumanReviewNoteRecord> history;
        EvidenceIntakeSnapshot accepted;
        try
        {
            history = await notes.ReadAsync(request.CenterRef, request.SubjectRef,
                request.SubjectKind, request.EvidenceRevision, ct);
            accepted = await evidence.ReadAsync(request.CenterRef, request.SubjectRef,
                request.SubjectKind, ct);
        }
        catch (InvalidOperationException)
        {
            // Both stored notes and accepted evidence have their own history,
            // digest and paired-audit integrity checks.
            return ReviewerProvenanceOutcome.ReviewNoteMissingOrCorrupt;
        }

        var note = history.Where(x => x.RequestId == request.ReviewNoteRequestId).ToArray();
        if (note.Length != 1 || accepted.Submissions.Length < request.EvidenceRevision)
            return ReviewerProvenanceOutcome.ReviewNoteMissingOrCorrupt;

        var acceptedClaim = accepted.Submissions[(int)request.EvidenceRevision - 1];
        if (acceptedClaim.CenterRef != request.CenterRef ||
            acceptedClaim.SubjectRef != request.SubjectRef ||
            acceptedClaim.SubjectKind != request.SubjectKind ||
            !IntakeDomain.IsSyntheticRef(acceptedClaim.SubmittingActorRef) ||
            !IntakeDomain.IsSyntheticRef(acceptedClaim.OpaqueEvidenceRef) ||
            !string.Equals(note[0].ReviewerClaimRef, attested.ResolvedReviewerRef,
                StringComparison.Ordinal) ||
            !string.Equals(acceptedClaim.OpaqueEvidenceRef, attested.EvidenceSourceRef,
                StringComparison.Ordinal))
            return ReviewerProvenanceOutcome.EvidenceMismatch;

        // Same claimant must not review their own submitted evidence. Neither
        // the physician subject nor the alleged center delegate can attest
        // their own evidence as independent. Real COI policy still undecided.
        if (string.Equals(attested.ResolvedReviewerRef,
                acceptedClaim.SubmittingActorRef, StringComparison.Ordinal) ||
            string.Equals(attested.ResolvedReviewerRef,
                request.SubjectRef, StringComparison.Ordinal))
            return ReviewerProvenanceOutcome.ReviewerNotIndependent;

        // Fake-positive attestation is NEVER a human approval, legitimate
        // clinician verification, or permission to open a real review panel.
        return ReviewerProvenanceOutcome.PreconditionsPresentOnly;
    }
}
