using System.Collections.Immutable;

namespace Taranom.ClinicalEvidenceIntake;

// Strictly synthetic REVIEW INTAKE, not a verified license, valid contract,
// trusted center manager, patient consent, or permission to access data.
internal enum EvidenceSubjectKind
{
    CenterRepresentative = 1,
    Physician = 2
}

internal enum EvidenceKind
{
    CenterManagerAuthorityClaim = 1,
    PhysicianProfessionalQualificationClaim = 2,
    PhysicianTaranomServiceContractClaim = 3
}

internal enum EvidenceIntakeOutcome
{
    ReviewWorkItemCreated = 1,
    Replayed = 2,
    ConflictingReplay = 3,
    ScopeDenied = 4,
    Invalid = 5,
    StaleRevision = 6
}

internal readonly record struct EvidenceSubmission(
    Guid RequestId,
    long ExpectedRevision,
    string? CenterRef,
    string? SubjectRef,
    EvidenceSubjectKind SubjectKind,
    EvidenceKind EvidenceKind,
    string? SubmittingActorRef,
    string? OpaqueEvidenceRef);

// Submission means a claimant has presented a pointer to material requiring
// separate authoritative review. Never report Verified/Approved/Active.
internal readonly record struct ReviewWorkItem(
    long Revision,
    EvidenceKind EvidenceKind,
    string OpaqueEvidenceRef,
    string SubmittingActorRef);

internal readonly record struct EvidenceIntakeDecision(
    EvidenceIntakeOutcome Outcome, EvidenceIntakeSnapshot Snapshot);

internal sealed class EvidenceIntakeSnapshot
{
    private EvidenceIntakeSnapshot(
        string centerRef, string subjectRef, EvidenceSubjectKind subjectKind,
        long revision, ImmutableArray<EvidenceSubmission> submissions,
        ImmutableArray<ReviewWorkItem> reviewWorkItems)
    {
        CenterRef = centerRef;
        SubjectRef = subjectRef;
        SubjectKind = subjectKind;
        Revision = revision;
        Submissions = submissions;
        ReviewWorkItems = reviewWorkItems;
    }

    internal string CenterRef { get; }
    internal string SubjectRef { get; }
    internal EvidenceSubjectKind SubjectKind { get; }
    internal long Revision { get; }
    internal ImmutableArray<EvidenceSubmission> Submissions { get; }
    internal ImmutableArray<ReviewWorkItem> ReviewWorkItems { get; }

    internal static EvidenceIntakeSnapshot ForSyntheticSubject(
        string centerRef, string subjectRef, EvidenceSubjectKind kind)
    {
        if (!ClinicalEvidenceIntake.IsSyntheticRef(centerRef) ||
            !ClinicalEvidenceIntake.IsSyntheticRef(subjectRef) ||
            !Enum.IsDefined(kind))
            throw new ArgumentException("Valid synthetic center and subject references required.");

        return new EvidenceIntakeSnapshot(centerRef, subjectRef, kind, 0,
            ImmutableArray<EvidenceSubmission>.Empty,
            ImmutableArray<ReviewWorkItem>.Empty);
    }

    internal EvidenceIntakeSnapshot Append(EvidenceSubmission submission)
    {
        var next = checked(Revision + 1);
        return new EvidenceIntakeSnapshot(CenterRef, SubjectRef, SubjectKind, next,
            Submissions.Add(submission),
            ReviewWorkItems.Add(new ReviewWorkItem(next, submission.EvidenceKind,
                submission.OpaqueEvidenceRef!, submission.SubmittingActorRef!)));
    }
}

internal static class ClinicalEvidenceIntake
{
    internal static bool IsSyntheticRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(x =>
            char.IsAsciiLetterOrDigit(x) || x is '-' or '_');

    internal static EvidenceIntakeDecision Submit(
        EvidenceIntakeSnapshot? current, EvidenceSubmission input)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 ||
            !IsSyntheticRef(input.CenterRef) ||
            !IsSyntheticRef(input.SubjectRef) ||
            !IsSyntheticRef(input.SubmittingActorRef) ||
            !IsSyntheticRef(input.OpaqueEvidenceRef) ||
            !Enum.IsDefined(input.SubjectKind) ||
            !Enum.IsDefined(input.EvidenceKind) ||
            !Compatible(input.SubjectKind, input.EvidenceKind))
            return new(EvidenceIntakeOutcome.Invalid, current);

        // Deterministic idempotency scoped to one immutable subject history.
        // Replay after newer submissions must never roll state backwards.
        foreach (var previous in current.Submissions)
        {
            if (previous.RequestId != input.RequestId) continue;
            return previous == input
                ? new(EvidenceIntakeOutcome.Replayed, current)
                : new(EvidenceIntakeOutcome.ConflictingReplay, current);
        }

        // This is a data-integrity condition, NOT proof that the submitter
        // really owns this center. Genuine IdP/authority is still missing.
        if (!string.Equals(current.CenterRef, input.CenterRef,
                StringComparison.Ordinal) ||
            !string.Equals(current.SubjectRef, input.SubjectRef,
                StringComparison.Ordinal) ||
            current.SubjectKind != input.SubjectKind)
            return new(EvidenceIntakeOutcome.ScopeDenied, current);

        if (current.Revision == long.MaxValue)
            return new(EvidenceIntakeOutcome.Invalid, current);

        if (current.Revision != input.ExpectedRevision)
            return new(EvidenceIntakeOutcome.StaleRevision, current);

        return new(EvidenceIntakeOutcome.ReviewWorkItemCreated,
            current.Append(input));
    }

    private static bool Compatible(EvidenceSubjectKind subject, EvidenceKind kind) =>
        (subject == EvidenceSubjectKind.CenterRepresentative &&
         kind == EvidenceKind.CenterManagerAuthorityClaim) ||
        (subject == EvidenceSubjectKind.Physician &&
         kind is EvidenceKind.PhysicianProfessionalQualificationClaim
             or EvidenceKind.PhysicianTaranomServiceContractClaim);
}
