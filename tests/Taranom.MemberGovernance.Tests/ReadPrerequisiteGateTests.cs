using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

public sealed class ReadPrerequisiteGateTests
{
    private static ReadPrerequisiteSnapshot AllConfirmed => new(
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed);

    [Fact]
    public void DefaultUnconfiguredBoundaryFailsClosed()
    {
        Assert.Equal(ReadPrerequisiteResult.EvidenceMissing,
            ReadPrerequisiteGate.Evaluate(default));
    }

    [Fact]
    public void AllPresentReturnsOnlyPreconditionsNotAuthorization()
    {
        Assert.Equal(ReadPrerequisiteResult.PreconditionsPresent,
            ReadPrerequisiteGate.Evaluate(AllConfirmed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AnyMissingEvidencePreventsProgress(int field)
    {
        Assert.Equal(ReadPrerequisiteResult.EvidenceMissing,
            ReadPrerequisiteGate.Evaluate(WithField(field, EvidenceCheck.Missing)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AnyRejectedEvidencePreventsProgress(int field)
    {
        Assert.Equal(ReadPrerequisiteResult.Rejected,
            ReadPrerequisiteGate.Evaluate(WithField(field, EvidenceCheck.Rejected)));
    }

    [Fact]
    public void RejectionTakesPrecedenceOverMissingEvidence()
    {
        var snapshot = AllConfirmed with
        {
            AuthenticatedServerSession = EvidenceCheck.Missing,
            TenantAndResourcePermission = EvidenceCheck.Rejected
        };
        Assert.Equal(ReadPrerequisiteResult.Rejected,
            ReadPrerequisiteGate.Evaluate(snapshot));
    }

    [Fact]
    public void UnrecognizedFutureEvidenceValuesFailClosed()
    {
        var snapshot = AllConfirmed with { AuditableDecisionPath = (EvidenceCheck)42 };
        Assert.Equal(ReadPrerequisiteResult.EvidenceMissing,
            ReadPrerequisiteGate.Evaluate(snapshot));
    }

    [Fact]
    public void CareEvidenceCannotSubstituteForAnOperationsSpecificLawfulBasis()
    {
        var snapshot = AllConfirmed with { OperationSpecificLawfulBasis = EvidenceCheck.Missing };
        Assert.Equal(ReadPrerequisiteResult.EvidenceMissing,
            ReadPrerequisiteGate.Evaluate(snapshot));
    }

    private static ReadPrerequisiteSnapshot WithField(int field, EvidenceCheck status) =>
        field switch
        {
            0 => AllConfirmed with { AuthenticatedServerSession = status },
            1 => AllConfirmed with { VerifiedMemberSubject = status },
            2 => AllConfirmed with { TenantAndResourcePermission = status },
            3 => AllConfirmed with { OperationSpecificLawfulBasis = status },
            4 => AllConfirmed with { AuditableDecisionPath = status },
            5 => AllConfirmed with { AuthoritativeSource = status },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
}
