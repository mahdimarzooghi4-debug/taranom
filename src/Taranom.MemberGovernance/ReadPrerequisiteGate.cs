namespace Taranom.MemberGovernance;

// These are NOT JWT claims, authorization grants or a wire API schema. These values
// must eventually come from independently approved and verified server-side owners.
// No real adapter or trusted session provider is part of this package.
public enum EvidenceCheck
{
    Missing = 0,
    Confirmed = 1,
    Rejected = 2
}

public enum ReadPrerequisiteResult
{
    EvidenceMissing = 0,
    Rejected = 1,
    PreconditionsPresent = 2
}

public readonly record struct ReadPrerequisiteSnapshot(
    EvidenceCheck AuthenticatedServerSession,
    EvidenceCheck VerifiedMemberSubject,
    EvidenceCheck TenantAndResourcePermission,
    EvidenceCheck OperationSpecificLawfulBasis,
    EvidenceCheck AuditableDecisionPath,
    EvidenceCheck AuthoritativeSource);

public static class ReadPrerequisiteGate
{
    // PreconditionsPresent merely permits a later domain-owned adjudication attempt.
    // It NEVER authorizes a read or materializes a private payload.
    public static ReadPrerequisiteResult Evaluate(ReadPrerequisiteSnapshot snapshot)
    {
        EvidenceCheck[] checks =
        [
            snapshot.AuthenticatedServerSession,
            snapshot.VerifiedMemberSubject,
            snapshot.TenantAndResourcePermission,
            snapshot.OperationSpecificLawfulBasis,
            snapshot.AuditableDecisionPath,
            snapshot.AuthoritativeSource
        ];

        if (checks.Any(check => check == EvidenceCheck.Rejected))
            return ReadPrerequisiteResult.Rejected;

        // Includes missing values and unknown/invalid future enum values: fail closed.
        if (checks.Any(check => check != EvidenceCheck.Confirmed))
            return ReadPrerequisiteResult.EvidenceMissing;

        return ReadPrerequisiteResult.PreconditionsPresent;
    }
}
