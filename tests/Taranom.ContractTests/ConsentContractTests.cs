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
    public async Task A01_OnlyOwnTenantAndActorAuditCanBeRead()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        var token = TestContext.Current.CancellationToken;
        await ctx.Store.ChangeAsync(ctx.Principal, ctx.Subject, ctx.Tenant,
            SyntheticPurpose.CareCommunicationTest, ctx.Scope, 0, "test:audit-own-grant",
            true, ct: token);
        var own = await ctx.Store.ReadOwnAuditAsync(ctx.Principal, ctx.Subject, ctx.Tenant, ct: token);
        Assert.Contains(own.Items, x => x.Action == "GRANT_TEST" && x.Decision == "RECORDED");
        Assert.All(own.Items, x => Assert.Contains(x.Action, new[]
            { "ACCESS_POLICY_TEST", "GRANT_TEST", "WITHDRAW_TEST" }));

        var stranger = new SyntheticPrincipal("test:auditor-stranger",
            "test:member-stranger", ctx.Tenant, "MEMBER_TEST", true);
        var foreign = await ctx.Store.ReadOwnAuditAsync(stranger, stranger.Member,
            ctx.Tenant, ct: token);
        Assert.DoesNotContain(foreign.Items, x => x.Action == "GRANT_TEST");
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Store.ReadOwnAuditAsync(stranger, ctx.Subject, ctx.Tenant, ct: token));
        Assert.Equal("DENY", denied.Message);

        var sponsor = new SyntheticPrincipal("test:audit-sponsor",
            ctx.Subject, ctx.Tenant, "SPONSOR_TEST", true);
        denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Store.ReadOwnAuditAsync(sponsor, ctx.Subject, ctx.Tenant, ct: token));
        Assert.Equal("DENY", denied.Message);

        var otherTenant = new SyntheticPrincipal(ctx.Principal.Actor,
            ctx.Subject, "test:unrelated-tenant", "MEMBER_TEST", true);
        var isolated = await ctx.Store.ReadOwnAuditAsync(otherTenant, ctx.Subject,
            otherTenant.Tenant, ct: token);
        Assert.DoesNotContain(isolated.Items, x => x.Action == "GRANT_TEST");
    }

    [Fact]
    public async Task A01_BoundedKeysetAuditPagingAndInvalidCursor()
    {
        var c = await SetupAsync();
        await using var _ = c.DataSource;
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 7; i++)
            Assert.True((await c.Store.EvaluateAccessAsync(c.Principal, c.Subject, c.Tenant,
                "READ_OWN_TEST_PROFILE", ct: ct)).Allowed);
        var first = await c.Store.ReadOwnAuditAsync(c.Principal, c.Subject, c.Tenant,
            pageSize: 3, ct: ct);
        Assert.Equal(3, first.Items.Count);
        Assert.NotNull(first.Next);
        var second = await c.Store.ReadOwnAuditAsync(c.Principal, c.Subject, c.Tenant,
            pageSize: 3, after: first.Next, ct: ct);
        Assert.Equal(3, second.Items.Count);
        Assert.DoesNotContain(second.Items, x => first.Items.Any(y => x.AuditRef == y.AuditRef));
        var invalid = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            c.Store.ReadOwnAuditAsync(c.Principal, c.Subject, c.Tenant, pageSize: 101, ct: ct));
        Assert.Equal("pageSize", invalid.ParamName);
        invalid = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            c.Store.ReadOwnAuditAsync(c.Principal, c.Subject, c.Tenant, pageSize: 0, ct: ct));
        Assert.Equal("pageSize", invalid.ParamName);
        var cursorError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Store.ReadOwnAuditAsync(c.Principal, c.Subject, c.Tenant,
                after: new SyntheticAuditCursor(DateTimeOffset.UtcNow, Guid.Empty), ct: ct));
        Assert.Equal("INVALID_TEST_AUDIT_CURSOR", cursorError.Message);
    }

    [Fact]
    public async Task X08_CanarySecretsNeverAppearAsPlaintextAuditMetadataOrDenial()
    {
        var ctx = await SetupAsync();
        await using var _ = ctx.DataSource;
        // These are dummy canaries, not actual credentials or medical data.
        var canaryActor = "test:CANARY_FAKE_TOKEN_" + Guid.NewGuid().ToString("N");
        var canaryRequest = "test:CANARY_FAKE_NOTE_" + Guid.NewGuid().ToString("N");
        var canaryTenant = "test:CANARY_FAKE_TENANT_" + Guid.NewGuid().ToString("N");
        var subject = "test:member-canary-" + Guid.NewGuid().ToString("N");
        var actor = new SyntheticPrincipal(canaryActor, subject, canaryTenant, "MEMBER_TEST", true);
        var token = TestContext.Current.CancellationToken;
        var allowed = await ctx.Store.EvaluateAccessAsync(actor, subject, canaryTenant,
            "READ_OWN_TEST_CONSENT", ct: token);
        Assert.True(allowed.Allowed);
        var grant = await ctx.Store.ChangeAsync(actor, subject, canaryTenant,
            SyntheticPurpose.HealthDataShareTest, ctx.Scope, 0, canaryRequest, true, ct: token);
        Assert.Equal(1, grant.Version);
        var forbidden = await ctx.Store.EvaluateAccessAsync(actor, subject, canaryTenant,
            "test:CANARY_FAKE_SECRET_ACTION", ct: token);
        Assert.False(forbidden.Allowed);
        Assert.Equal("ACTION_DENIED", forbidden.Reason);
        Assert.DoesNotContain("CANARY", forbidden.ToString(), StringComparison.Ordinal);

        await using var connection = await ctx.DataSource.OpenConnectionAsync(token);
        await using var query = new NpgsqlCommand("""
            SELECT actor_ref, tenant_ref, correlation_ref, action_code, decision_code
            FROM synthetic_audit WHERE tenant_ref=@tenant
            """, connection);
        query.Parameters.AddWithValue("tenant", SyntheticConsentStore.AuditRef(canaryTenant));
        await using var reader = await query.ExecuteReaderAsync(token);
        var rows = 0;
        while (await reader.ReadAsync(token))
        {
            rows++;
            var actorStored = reader.GetString(0);
            var tenantStored = reader.GetString(1);
            var correlationStored = reader.GetString(2);
            Assert.StartsWith("test:sha256-", actorStored);
            Assert.Equal(SyntheticConsentStore.AuditRef(canaryTenant), tenantStored);
            Assert.DoesNotContain("CANARY", actorStored, StringComparison.Ordinal);
            Assert.DoesNotContain("CANARY", tenantStored, StringComparison.Ordinal);
            Assert.DoesNotContain("CANARY", correlationStored, StringComparison.Ordinal);
            Assert.DoesNotContain("CANARY", reader.GetString(3), StringComparison.Ordinal);
            Assert.DoesNotContain("CANARY", reader.GetString(4), StringComparison.Ordinal);
        }
        // One explicit ALLOW, one mutation-authorization ALLOW, one domain GRANT and one DENY.
        Assert.Equal(4, rows);
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
