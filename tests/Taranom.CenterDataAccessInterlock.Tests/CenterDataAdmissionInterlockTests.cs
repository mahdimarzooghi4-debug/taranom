using Taranom.CenterAuthorityBoundary;
using Taranom.CenterDataAccessInterlock;
using Xunit;

namespace Taranom.CenterDataAccessInterlock.Tests;

public sealed class CenterDataAdmissionInterlockTests
{
    private const string Center = "test:center-a";
    private const string Actor = "test:manager-a";
    private const string Tenant = "test:tenant-a";

    private sealed class ForbiddenPort : ICenterSensitiveDataPort
    {
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }

        public Task ReadAsync(CenterAuthorityRequest request, CancellationToken cancellationToken)
        {
            Reads++;
            throw new InvalidOperationException("INTERNAL_TEST_PROTECTED_READ_CALLED");
        }
        public Task WriteAsync(CenterAuthorityRequest request, CancellationToken cancellationToken)
        {
            Writes++;
            throw new InvalidOperationException("INTERNAL_TEST_PROTECTED_WRITE_CALLED");
        }
    }

    private sealed class SyntheticSource(CenterServerEvidence? evidence) : ICenterServerEvidenceSource
    {
        public Task<CenterServerEvidence?> ResolveFromServerAsync(
            Guid attemptRef, CenterReadWriteOperation operation, CancellationToken cancellationToken) =>
            Task.FromResult(evidence);
    }

    private static CenterAuthorityRequest Request(CenterReadWriteOperation op,
        string center = Center) => new(Guid.NewGuid(), op, center,
            null, null, null, null);

    private static CenterServerEvidence AllConfirmed(CenterReadWriteOperation op) => new(
        Actor, Actor, Actor, Center, Center,
        Tenant, Tenant, Tenant,
        "test:permission-v1", "test:permission-v1",
        CenterActorCategory.CenterRepresentative, op,
        CenterEvidenceCheck.Confirmed, CenterEvidenceCheck.Confirmed,
        CenterEvidenceCheck.Confirmed, CenterEvidenceCheck.Confirmed,
        CenterEvidenceCheck.Confirmed, CenterEvidenceCheck.Confirmed,
        CenterEvidenceCheck.Confirmed);

    private static async Task<CenterOperationRejection> Check(
        CenterAuthorityGate gate, CenterAuthorityRequest request, ForbiddenPort port)
    {
        var boundary = new CenterDataAdmissionInterlock(gate, port);
        Assert.False(boundary.HasOperationalDataPath);
        var result = await boundary.HandleAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(0, port.Reads);
        Assert.Equal(0, port.Writes);
        return result;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task DisconnectedOperationNeverReachesDataPort(int code)
    {
        var port = new ForbiddenPort();
        var op = (CenterReadWriteOperation)code;
        var r = await Check(new CenterAuthorityGate(), Request(op), port);
        Assert.Equal(CenterOperationDisposition.NotConnected, r.Disposition);
        Assert.Equal(CenterAuthorityResult.AuthoritativeSourceUnavailable, r.EvidenceReason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task AllPositiveTestEvidenceIsStillNotProtectedAuthorization(int code)
    {
        var port = new ForbiddenPort();
        var op = (CenterReadWriteOperation)code;
        var source = new SyntheticSource(AllConfirmed(op));
        var r = await Check(new CenterAuthorityGate(source), Request(op), port);
        Assert.Equal(CenterOperationDisposition.NotConnected, r.Disposition);
        Assert.Equal(CenterAuthorityResult.PreconditionsPresentOnly, r.EvidenceReason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task FakeBrowserAdminRoleNeverActivatesDataPath(int code)
    {
        var port = new ForbiddenPort();
        var op = (CenterReadWriteOperation)code;
        var rq = Request(op) with {
            BrowserRoleHint = "admin",
            BrowserActorHint = Actor,
            BrowserCenterHint = Center,
            BrowserTenantHint = Tenant
        };
        var r = await Check(new CenterAuthorityGate(new SyntheticSource(AllConfirmed(op))), rq, port);
        Assert.Equal(CenterOperationDisposition.NotConnected, r.Disposition);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task CrossCenterRequestsCannotAccessPrivatePort(int code)
    {
        var port = new ForbiddenPort();
        var op = (CenterReadWriteOperation)code;
        var r = await Check(new CenterAuthorityGate(new SyntheticSource(AllConfirmed(op))),
            Request(op, "test:other-center"), port);
        Assert.Equal(CenterOperationDisposition.Denied, r.Disposition);
        Assert.Equal(CenterAuthorityResult.CenterScopeDenied, r.EvidenceReason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task RevokedGrantsCannotAccessProtectedPort(int code)
    {
        var port = new ForbiddenPort();
        var op = (CenterReadWriteOperation)code;
        var evidence = AllConfirmed(op) with { GrantNotRevoked = CenterEvidenceCheck.Rejected };
        var r = await Check(new CenterAuthorityGate(new SyntheticSource(evidence)), Request(op), port);
        Assert.Equal(CenterOperationDisposition.Denied, r.Disposition);
        Assert.Equal(CenterAuthorityResult.PermissionStaleOrRevoked, r.EvidenceReason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task AbsentAuditCannotAccessProtectedPort(int code)
    {
        var port = new ForbiddenPort();
        var op = (CenterReadWriteOperation)code;
        var evidence = AllConfirmed(op) with { AuditReady = CenterEvidenceCheck.Missing };
        var r = await Check(new CenterAuthorityGate(new SyntheticSource(evidence)), Request(op), port);
        Assert.Equal(CenterOperationDisposition.Denied, r.Disposition);
        Assert.Equal(CenterAuthorityResult.AuditUnavailable, r.EvidenceReason);
    }

    [Fact]
    public async Task MalformedRequestsDoNotLeakPrivateData()
    {
        var port = new ForbiddenPort();
        var result = await Check(new CenterAuthorityGate(), Request(CenterReadWriteOperation.ReadAffiliations,
            "national-id:1234"), port);
        Assert.Equal(CenterOperationDisposition.InvalidRequest, result.Disposition);
        Assert.Equal(CenterAuthorityResult.InvalidRequest, result.EvidenceReason);
    }

    [Fact]
    public void CannotInjectNullGateOrRepository()
    {
        var port = new ForbiddenPort();
        Assert.Throws<ArgumentNullException>(() => new CenterDataAdmissionInterlock(null!, port));
        Assert.Throws<ArgumentNullException>(() => new CenterDataAdmissionInterlock(new CenterAuthorityGate(), null!));
    }

    [Fact]
    public void ThereIsNoSuccessOrProtectedPayloadContract()
    {
        Assert.DoesNotContain(Enum.GetNames<CenterOperationDisposition>(),
            name => name is "Allowed" or "Authorized" or "DataReady" or "Success");
        Assert.DoesNotContain(typeof(CenterOperationRejection).GetProperties(),
            property => property.Name is "Data" or "Payload" or "Doctor" or "CenterSnapshot");
    }
}
