using Npgsql;
using Taranom.CenterAuthorityBoundary;
using Taranom.CenterDataAccessInterlock;
using Taranom.ClinicalCenterGovernance;
using Taranom.ClinicalCenterJournal.Postgres;
using Xunit;

namespace Taranom.CenterDataAccessInterlock.IntegrationTests;

// PostgreSQL is genuine, but every ID, role, user and credential is synthetic.
// The interlock MUST prevent reaching this sensitive test journal.
public sealed class ProtectedCenterDatabaseAdmissionTests
{
    private static readonly Lazy<Task> Ready = new(InitializeAsync);
    private static string PgConnection => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG is required for synthetic PostgreSQL");
    private const string Doctor = "test:doctor-a";
    private const string Ward = "test:ward-a";

    private static async Task InitializeAsync()
    {
        await using var data = NpgsqlDataSource.Create(PgConnection);
        await PgClinicalCenterJournal.InitializeIsolatedTestSchemaAsync(
            data, TestContext.Current.CancellationToken);
    }

    private static string NewCenter() => "test:center-" + Guid.NewGuid().ToString("N");

    private static CenterAuthorityRequest Request(string center, CenterReadWriteOperation op) =>
        new(Guid.NewGuid(), op, center, null, null, null, null);

    private static CenterServerEvidence Positive(string center, CenterReadWriteOperation op) =>
        new("test:actor-a", "test:actor-a", "test:actor-a", center, center,
            "test:tenant-a", "test:tenant-a", "test:tenant-a",
            "test:grant-v1", "test:grant-v1", CenterActorCategory.CenterRepresentative, op,
            CenterEvidenceCheck.Confirmed, CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed, CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed, CenterEvidenceCheck.Confirmed,
            CenterEvidenceCheck.Confirmed);

    private sealed class SyntheticAuthoritySource(CenterServerEvidence? evidence)
        : ICenterServerEvidenceSource
    {
        public Task<CenterServerEvidence?> ResolveFromServerAsync(
            Guid attemptRef, CenterReadWriteOperation operation, CancellationToken ct) =>
            Task.FromResult(evidence);
    }

    private sealed class SensitivePostgresPort(
        PgClinicalCenterJournal journal, string center) : ICenterSensitiveDataPort
    {
        internal int ReadAttempts { get; private set; }
        internal int WriteAttempts { get; private set; }
        public async Task ReadAsync(CenterAuthorityRequest request, CancellationToken ct)
        {
            ReadAttempts++;
            await journal.ReadAsync(center, ct);
        }
        public async Task WriteAsync(CenterAuthorityRequest request, CancellationToken ct)
        {
            WriteAttempts++;
            // A deliberately detectable extra relational event if this
            // dangerous port is ever invoked, including on read requests.
            var snapshot = await journal.ReadAsync(center, ct);
            var mutation = new CenterCommand(Guid.NewGuid(), snapshot.Version,
                CenterCommandKind.DefineDepartment, center, center,
                "test:must-never-write", null, CenterEvidence.Confirmed,
                CenterEvidence.Confirmed);
            await journal.ApplyAsync(center, mutation, ct);
        }
    }

    private static async Task<(NpgsqlDataSource Db, PgClinicalCenterJournal Journal, string Center)> SeedAsync()
    {
        await Ready.Value.WaitAsync(TestContext.Current.CancellationToken);
        var data = NpgsqlDataSource.Create(PgConnection);
        var journal = new PgClinicalCenterJournal(data);
        var center = NewCenter();
        var first = new CenterCommand(Guid.NewGuid(), 0,
            CenterCommandKind.DefineDepartment, center, center,
            Ward, null, CenterEvidence.Confirmed, CenterEvidence.Confirmed);
        var second = new CenterCommand(Guid.NewGuid(), 1,
            CenterCommandKind.RecordDoctorAffiliation, center, center,
            null, Doctor, CenterEvidence.Confirmed, CenterEvidence.Confirmed);
        Assert.Equal(CenterOutcome.Applied,
            (await journal.ApplyAsync(center, first, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(CenterOutcome.Applied,
            (await journal.ApplyAsync(center, second, TestContext.Current.CancellationToken)).Outcome);
        return (data, journal, center);
    }

    private static async Task VerifyUnchanged(PgClinicalCenterJournal journal, string center,
        SensitivePostgresPort spy)
    {
        Assert.Equal(0, spy.ReadAttempts);
        Assert.Equal(0, spy.WriteAttempts);
        var state = await journal.ReadAsync(center, TestContext.Current.CancellationToken);
        Assert.Equal(2L, state.Version);
        Assert.Single(state.Departments);
        Assert.Single(state.Doctors);
        Assert.Equal(DoctorAffiliation.PendingIndependentVerification, state.Doctors[Doctor]);
        Assert.Equal(2, state.Events.Length);
        Assert.DoesNotContain("test:must-never-write", state.Departments);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task SyntheticPositiveChecksCannotReadOrChangeRealPostgres(int opcode)
    {
        var (data, journal, center) = await SeedAsync();
        await using var _ = data;
        var op = (CenterReadWriteOperation)opcode;
        var spy = new SensitivePostgresPort(journal, center);
        var gate = new CenterAuthorityGate(new SyntheticAuthoritySource(Positive(center, op)));
        var response = await new CenterDataAdmissionInterlock(gate, spy).HandleAsync(
            Request(center, op), TestContext.Current.CancellationToken);
        Assert.Equal(CenterOperationDisposition.NotConnected, response.Disposition);
        Assert.Equal(CenterAuthorityResult.PreconditionsPresentOnly, response.EvidenceReason);
        await VerifyUnchanged(journal, center, spy);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task WithoutIdentityProviderExistingPostgresRemainsPrivate(int opcode)
    {
        var (data, journal, center) = await SeedAsync();
        await using var _ = data;
        var spy = new SensitivePostgresPort(journal, center);
        var response = await new CenterDataAdmissionInterlock(
            new CenterAuthorityGate(), spy).HandleAsync(
                Request(center, (CenterReadWriteOperation)opcode) with {
                    BrowserRoleHint = "center-admin",
                    BrowserActorHint = "test:actor-a",
                    BrowserCenterHint = center
                }, TestContext.Current.CancellationToken);
        Assert.Equal(CenterOperationDisposition.NotConnected, response.Disposition);
        Assert.Equal(CenterAuthorityResult.AuthoritativeSourceUnavailable, response.EvidenceReason);
        await VerifyUnchanged(journal, center, spy);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task CrossCenterSpoofCannotAccessStoredCenterData(int opcode)
    {
        var (data, journal, center) = await SeedAsync();
        await using var _ = data;
        var op = (CenterReadWriteOperation)opcode;
        var spy = new SensitivePostgresPort(journal, center);
        var response = await new CenterDataAdmissionInterlock(
            new CenterAuthorityGate(new SyntheticAuthoritySource(Positive(center, op))), spy)
            .HandleAsync(Request("test:other-center", op),
                TestContext.Current.CancellationToken);
        Assert.Equal(CenterOperationDisposition.Denied, response.Disposition);
        Assert.Equal(CenterAuthorityResult.CenterScopeDenied, response.EvidenceReason);
        await VerifyUnchanged(journal, center, spy);
    }

    [Fact]
    public async Task MissingCurrentGrantDoesNotLeakRelationalData()
    {
        var (data, journal, center) = await SeedAsync();
        await using var _ = data;
        var spy = new SensitivePostgresPort(journal, center);
        var op = CenterReadWriteOperation.ReadAffiliations;
        var revoked = Positive(center, op) with {
            CurrentPermissionVersionRef = "test:permission-old"
        };
        var r = await new CenterDataAdmissionInterlock(
            new CenterAuthorityGate(new SyntheticAuthoritySource(revoked)), spy)
            .HandleAsync(Request(center, op), TestContext.Current.CancellationToken);
        Assert.Equal(CenterOperationDisposition.Denied, r.Disposition);
        Assert.Equal(CenterAuthorityResult.PermissionStaleOrRevoked, r.EvidenceReason);
        await VerifyUnchanged(journal, center, spy);
    }
}
