using Taranom.CenterAuthorityBoundary;
using Xunit;

namespace Taranom.CenterAuthorityBoundary.Tests;

public sealed class CenterAuthorityGateTests
{
    private const string Center = "test:center-a";
    private const string Manager = "test:manager-a";
    private const string Tenant = "test:tenant-a";

    private sealed class TestSource(CenterServerEvidence? next = null) : ICenterServerEvidenceSource
    {
        public int Calls { get; private set; }
        public CenterServerEvidence? Evidence { get; set; } = next;
        public bool Fail { get; set; }
        public async Task<CenterServerEvidence?> ResolveFromServerAsync(
            Guid attemptRef, CenterReadWriteOperation operation, CancellationToken ct)
        {
            Calls++;
            await Task.Yield();
            if (Fail) throw new InvalidOperationException("provider secret must not leak");
            return Evidence;
        }
    }

    private static CenterAuthorityRequest Request(
        CenterReadWriteOperation op = CenterReadWriteOperation.DefineDepartment) =>
        new(Guid.NewGuid(), op, Center, null, null, null, null);

    private static CenterServerEvidence Complete(
        CenterReadWriteOperation op = CenterReadWriteOperation.DefineDepartment) =>
        new(
            Manager, Manager, Manager, Center, Center,
            Tenant, Tenant, Tenant,
            "test:grant-v1", "test:grant-v1",
            CenterActorCategory.CenterRepresentative, op,
            CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed);

    private static async Task<CenterAuthorityResult> Assess(
        CenterServerEvidence evidence,
        CenterAuthorityRequest? request = null)
    {
        var source = new TestSource(evidence);
        return await new CenterAuthorityGate(source).AssessAsync(request ?? Request());
    }

    [Fact]
    public async Task DefaultGateNeverUsesBrowserClaimsAsIdentity()
    {
        var request = Request() with
        {
            BrowserRoleHint = "admin",
            BrowserActorHint = Manager,
            BrowserCenterHint = Center,
            BrowserTenantHint = Tenant
        };
        Assert.Equal(CenterAuthorityResult.AuthoritativeSourceUnavailable,
            await new CenterAuthorityGate().AssessAsync(request));
    }

    [Fact]
    public async Task EvenCompleteSyntheticServerChecksAreNotAnAuthorizationGrant()
    {
        Assert.Equal(CenterAuthorityResult.PreconditionsPresentOnly,
            await Assess(Complete()));
        Assert.DoesNotContain(Enum.GetNames<CenterAuthorityResult>(),
            name => name is "Authorized" or "Granted" or "Allowed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task EachDistinctReadAndWriteOperationMayOnlyMeetPreconditions(int selection)
    {
        var op = (CenterReadWriteOperation)(selection + 1);
        Assert.Equal(CenterAuthorityResult.PreconditionsPresentOnly,
            await Assess(Complete(op), Request(op)));
    }

    [Fact]
    public async Task NoIdentitySourceMeansNoPrivilegedDataOrWrites()
    {
        var source = new TestSource();
        var result = await new CenterAuthorityGate(source).AssessAsync(Request());
        Assert.Equal(CenterAuthorityResult.AuthoritativeSourceUnavailable, result);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ProviderOutageFailsClosedWithoutLeakingInternalDetail()
    {
        var source = new TestSource(Complete()) { Fail = true };
        Assert.Equal(CenterAuthorityResult.AuthoritativeSourceUnavailable,
            await new CenterAuthorityGate(source).AssessAsync(Request()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task MissingRejectedOrUnknownIssuerEvidenceFails(int value)
    {
        Assert.Equal(CenterAuthorityResult.IdentityNotVerified,
            await Assess(Complete() with { IssuerVerified = (CenterEvidenceCheck)value }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task ExpiredRevokedOrUnknownSessionFails(int value)
    {
        Assert.Equal(CenterAuthorityResult.IdentityNotVerified,
            await Assess(Complete() with { SessionCurrent = (CenterEvidenceCheck)value }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task InvalidIdentityCenterManagerMappingFails(int value)
    {
        Assert.Equal(CenterAuthorityResult.IdentityNotVerified,
            await Assess(Complete() with {
                IdentityMappingCurrent = (CenterEvidenceCheck)value }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(999)]
    public async Task SponsorEmployerDoctorAdminAndUnknownCannotInheritManagerRole(int kind)
    {
        Assert.Equal(CenterAuthorityResult.ManagerAuthorityDenied,
            await Assess(Complete() with { ActorCategory = (CenterActorCategory)kind }));
    }

    [Fact]
    public async Task AnotherPrincipalCannotUseTheCentersManagerGrant()
    {
        Assert.Equal(CenterAuthorityResult.ManagerAuthorityDenied,
            await Assess(Complete() with { DelegatedManagerRef = "test:manager-b" }));
    }

    [Fact]
    public async Task ServerSubjectMismatchFailsBeforeManagerScope()
    {
        Assert.Equal(CenterAuthorityResult.IdentityNotVerified,
            await Assess(Complete() with { SessionSubjectRef = "test:manager-b" }));
    }

    [Fact]
    public async Task ServerSelectedAnotherCenterRejectsClaimedCenter()
    {
        Assert.Equal(CenterAuthorityResult.CenterScopeDenied,
            await Assess(Complete() with { ResolvedCenterRef = "test:center-b" }));
    }

    [Fact]
    public async Task CrossCenterResourceOwnerCannotBeReadOrWritten()
    {
        Assert.Equal(CenterAuthorityResult.CenterScopeDenied,
            await Assess(Complete() with { ResourceCenterRef = "test:center-b" }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CrossTenantMismatchFailsClosed(int which)
    {
        var data = Complete();
        data = which switch
        {
            0 => data with { SessionTenantRef = "test:tenant-b" },
            1 => data with { CenterTenantRef = "test:tenant-b" },
            _ => data with { ResourceTenantRef = "test:tenant-b" }
        };
        Assert.Equal(CenterAuthorityResult.CenterScopeDenied, await Assess(data));
    }

    [Fact]
    public async Task BrowserCenterSpoofCannotOverrideResolvedCenter()
    {
        Assert.Equal(CenterAuthorityResult.CenterScopeDenied,
            await Assess(Complete(), Request() with { BrowserCenterHint = "test:center-b" }));
    }

    [Fact]
    public async Task BrowserActorAndTenantSpoofsCannotSelectScope()
    {
        Assert.Equal(CenterAuthorityResult.CenterScopeDenied,
            await Assess(Complete(), Request() with { BrowserActorHint = "test:manager-b" }));
        Assert.Equal(CenterAuthorityResult.CenterScopeDenied,
            await Assess(Complete(), Request() with { BrowserTenantHint = "test:tenant-b" }));
    }

    [Fact]
    public async Task MatchingBrowserClaimsStillNeverAuthorize()
    {
        Assert.Equal(CenterAuthorityResult.PreconditionsPresentOnly,
            await Assess(Complete(), Request() with
            {
                BrowserRoleHint = "admin",
                BrowserCenterHint = Center,
                BrowserActorHint = Manager,
                BrowserTenantHint = Tenant
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task MissingRevokedOrUnknownManagerGrantDenied(int value)
    {
        Assert.Equal(CenterAuthorityResult.ManagerAuthorityDenied,
            await Assess(Complete() with
            { ManagerAssignmentCurrent = (CenterEvidenceCheck)value }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task MissingRevokedOrUnknownOperationGrantDenied(int value)
    {
        Assert.Equal(CenterAuthorityResult.OperationNotPermitted,
            await Assess(Complete() with { OperationScopeValid = (CenterEvidenceCheck)value }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task RevokedOrUnavailableGrantCannotMeetPrerequisites(int value)
    {
        Assert.Equal(CenterAuthorityResult.PermissionStaleOrRevoked,
            await Assess(Complete() with { GrantNotRevoked = (CenterEvidenceCheck)value }));
    }

    [Fact]
    public async Task StalePermissionVersionBlocksOtherwiseCompleteChecks()
    {
        Assert.Equal(CenterAuthorityResult.PermissionStaleOrRevoked,
            await Assess(Complete() with
            { PreviousPermissionVersionRef = "test:grant-old" }));
    }

    [Fact]
    public async Task PermissionForReadingDoesNotAllowDefiningADepartment()
    {
        Assert.Equal(CenterAuthorityResult.OperationNotPermitted,
            await Assess(Complete(CenterReadWriteOperation.ReadDepartments)));
    }

    [Fact]
    public async Task PermissionForDefiningDepartmentDoesNotAllowDoctorChanges()
    {
        var op = CenterReadWriteOperation.RecordDoctorAffiliation;
        Assert.Equal(CenterAuthorityResult.OperationNotPermitted,
            await Assess(Complete(), Request(op)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task MissingOrRejectedAuditBlocksReadAndWrite(int value)
    {
        Assert.Equal(CenterAuthorityResult.AuditUnavailable,
            await Assess(Complete() with { AuditReady = (CenterEvidenceCheck)value }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("real:doctor-number")]
    [InlineData("test:")]
    [InlineData("test:parent/child")]
    [InlineData("test:center with space")]
    public async Task RealMalformedAndNetworkIdentifiersCannotPass(string center)
    {
        var source = new TestSource(Complete());
        Assert.Equal(CenterAuthorityResult.InvalidRequest,
            await new CenterAuthorityGate(source).AssessAsync(
                Request() with { RequestedCenterRef = center }));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task DefaultAndUnknownOperationsCannotPass()
    {
        var source = new TestSource(Complete());
        Assert.Equal(CenterAuthorityResult.InvalidRequest,
            await new CenterAuthorityGate(source).AssessAsync(default));
        Assert.Equal(CenterAuthorityResult.InvalidRequest,
            await new CenterAuthorityGate(source).AssessAsync(
                Request() with { Operation = (CenterReadWriteOperation)100 }));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task UnknownServerActorOrNonSyntheticEvidenceCannotPass()
    {
        Assert.Equal(CenterAuthorityResult.IdentityNotVerified,
            await Assess(Complete() with { ResolvedActorRef = "national-identity" }));
        Assert.Equal(CenterAuthorityResult.IdentityNotVerified,
            await Assess(Complete() with { CurrentPermissionVersionRef = null }));
    }

    [Fact]
    public async Task CancelledOperationHonorsCancellationWithoutAuthorizing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var source = new CancelledSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new CenterAuthorityGate(source).AssessAsync(Request(), cts.Token));
    }

    private sealed class CancelledSource : ICenterServerEvidenceSource
    {
        public Task<CenterServerEvidence?> ResolveFromServerAsync(
            Guid attemptRef, CenterReadWriteOperation operation, CancellationToken ct) =>
            Task.FromCanceled<CenterServerEvidence?>(ct);
    }
}
