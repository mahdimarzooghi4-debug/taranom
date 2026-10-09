using Npgsql;
using Taranom.SyntheticFoundation;
using Xunit;

namespace Taranom.ContractTests;

// Actual PostgreSQL integration tests; independently named cases with isolated test: prefixes.
public sealed class ConsentContractTests
{
    private static async Task<(NpgsqlDataSource DataSource, SyntheticConsentStore Store,
        SyntheticPrincipal Principal, string Subject, string Tenant, string Scope)> SetupAsync()
    {
        var connection = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
            ?? throw new InvalidOperationException("Missing isolated PostgreSQL CI test connection");
        var unique = Guid.NewGuid().ToString("N");
        var tenant = "test:t-" + unique;
        var subject = "test:m-" + unique;
        var principal = new SyntheticPrincipal("test:a-" + unique, subject, tenant, "MEMBER_TEST", true);
        var source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection)
        {
            Username = "taranom_sprint01_runtime",
            Password = "only_ephemeral_synthetic_test"
        }.ConnectionString);
        // Test bootstrap is executed first by the existing smoke suite on an ephemeral CI database.
        return (source, new SyntheticConsentStore(source), principal, subject, tenant, "test:private");
    }

    [Fact]
    public async Task C01_CareConsentDoesNotAuthorizeResearch()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, 0, "test:care-grant", true, ct: TestContext.Current.CancellationToken);
        Assert.False(await ctx.Store.EvaluateAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.SecondaryAiResearchTest, ctx.Scope, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task C02_WrongScopeIsDenied()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, 0, "test:scope-grant", true, ct: TestContext.Current.CancellationToken);
        Assert.False(await ctx.Store.EvaluateAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, "test:other-scope", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task C02_WrongSubjectIsDenied()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, 0, "test:subject-grant", true, ct: TestContext.Current.CancellationToken);
        Assert.False(await ctx.Store.EvaluateAsync(ctx.Principal, "test:other-member",
            ctx.Tenant, SyntheticPurpose.CareCommunicationTest, ctx.Scope, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task C03_ExactConsentGrantIsAllowed()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        var result = await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.HealthDataShareTest, ctx.Scope, 0, "test:share-grant", true, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, result.Version);
        Assert.False(result.Replayed);
        Assert.True(await ctx.Store.EvaluateAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.HealthDataShareTest, ctx.Scope, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task C04_WithdrawalAppendsNewVersionAndDenies()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, 0, "test:withdraw-grant", true, ct: TestContext.Current.CancellationToken);
        var withdraw = await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, 1, "test:withdraw", false, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, withdraw.Version);
        Assert.False(await ctx.Store.EvaluateAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task C05_MissingConsentIsDenied()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        Assert.False(await ctx.Store.EvaluateAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.SecondaryAiResearchTest, ctx.Scope, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task C06_UnknownCostOrPermissionTypeCannotCreateResearchConsent()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
                (SyntheticPurpose)909, ctx.Scope, 0, "test:cost-choice", true, ct: TestContext.Current.CancellationToken));
        Assert.Equal("INVALID_PURPOSE", error.Message);
        Assert.False(await ctx.Store.EvaluateAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.SecondaryAiResearchTest, ctx.Scope, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A05_IdenticalReplayDoesNotAdvanceState()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        var a = await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.HealthDataShareTest, ctx.Scope, 0, "test:replay-key", true, ct: TestContext.Current.CancellationToken);
        var b = await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.HealthDataShareTest, ctx.Scope, 0, "test:replay-key", true, ct: TestContext.Current.CancellationToken);
        Assert.False(a.Replayed);
        Assert.True(b.Replayed);
        Assert.Equal(a.Version, b.Version);
    }

    [Fact]
    public async Task X02_ChangedPayloadOnSameRequestKeyFails()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.HealthDataShareTest, ctx.Scope, 0, "test:collision", true, ct: TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
                SyntheticPurpose.HealthDataShareTest, ctx.Scope, 1, "test:collision", true, ct: TestContext.Current.CancellationToken));
        Assert.Equal("IDEMPOTENCY_CONFLICT", error.Message);
    }

    [Fact]
    public async Task X01_ConcurrentSameVersionOnlyOneWriterWins()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        var tasks = new[] { "test:race-one", "test:race-two" }.Select(async request =>
        {
            try
            {
                await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
                    SyntheticPurpose.CareCommunicationTest, ctx.Scope, 0, request, true, ct: TestContext.Current.CancellationToken);
                return true;
            }
            catch (InvalidOperationException e) when (e.Message == "VERSION_CONFLICT")
            {
                return false;
            }
        });
        var result = await Task.WhenAll(tasks);
        Assert.Single(result, x => x);
    }

    [Fact]
    public async Task X05_SameMemberIdentifierDifferentTenantIsIsolated()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, 0, "test:grant-main", true, ct: TestContext.Current.CancellationToken);
        var otherTenant = "test:other-" + Guid.NewGuid().ToString("N");
        var second = new SyntheticPrincipal("test:actor-other", ctx.Subject,
            otherTenant, "MEMBER_TEST", true);
        Assert.False(await ctx.Store.EvaluateAsync(second, ctx.Subject, otherTenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, ct: TestContext.Current.CancellationToken));
        await ctx.Store.ChangeAsync(second, ctx.Subject, otherTenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, 0, "test:grant-other", true, ct: TestContext.Current.CancellationToken);
        Assert.True(await ctx.Store.EvaluateAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A04_AccessAllowAndDenyAreAuditedByTenant()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        var owner = await ctx.Store.EvaluateAccessAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            "READ_OWN_TEST_CONSENT", ct: TestContext.Current.CancellationToken);
        var stranger = await ctx.Store.EvaluateAccessAsync(ctx.Principal, "test:not-owner",
            ctx.Tenant, "READ_OWN_TEST_CONSENT", ct: TestContext.Current.CancellationToken);
        Assert.True(owner.Allowed);
        Assert.False(stranger.Allowed);
        Assert.Equal(1, await ctx.Store.AccessAuditCountAsync(ctx.Tenant, "ALLOW", ct: TestContext.Current.CancellationToken));
        Assert.Equal(1, await ctx.Store.AccessAuditCountAsync(ctx.Tenant, "DENY", ct: TestContext.Current.CancellationToken));
    }
}
