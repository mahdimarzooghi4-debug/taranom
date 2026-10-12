using Npgsql;
using Taranom.MemberGovernance;
using Taranom.ProofingJournal.Postgres;
using Xunit;

namespace Taranom.ProofingJournal.IntegrationTests;

// Uses the same ordered xUnit collection as the previous proofing suites.
// The three synthetic fixtures create PostgreSQL roles and ACLs at bootstrap.
[Collection("Proofing isolated PostgreSQL schema")]
public sealed class CostAwarenessJournalDatabaseTests
{
    private static readonly Lazy<Task> Ready = new(BootstrapAsync);
    private const string RuntimeRole = "taranom_sprint29_cost_test";
    private const string RuntimePassword = "only_ephemeral_cost_journal_test";
    private static string AdminConnection =>
        Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("Isolated TARANOM_TEST_PG is required.");

    private static async Task BootstrapAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var owner = NpgsqlDataSource.Create(AdminConnection);
        await PgSyntheticCostAwarenessJournal.InitializeIsolatedTestSchemaAsync(owner, ct);
        await using var connection = await owner.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            DO $block$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles
                WHERE rolname='taranom_sprint29_cost_test') THEN
                CREATE ROLE taranom_sprint29_cost_test LOGIN
                  PASSWORD 'only_ephemeral_cost_journal_test';
              END IF;
            END $block$;
            REVOKE ALL ON internal_test_cost_preview,
               internal_test_cost_choice_event, internal_test_cost_choice_audit
               FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO taranom_sprint29_cost_test;
            GRANT SELECT,INSERT ON internal_test_cost_preview
               TO taranom_sprint29_cost_test;
            GRANT UPDATE (choice_revision) ON internal_test_cost_preview
               TO taranom_sprint29_cost_test;
            GRANT SELECT,INSERT ON internal_test_cost_choice_event,
               internal_test_cost_choice_audit TO taranom_sprint29_cost_test;
            """, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(NpgsqlDataSource Db, PgSyntheticCostAwarenessJournal Store)>
        ConnectAsync()
    {
        await Ready.Value.WaitAsync(TestContext.Current.CancellationToken);
        var cs = new NpgsqlConnectionStringBuilder(AdminConnection)
        {
            Username = RuntimeRole,
            Password = RuntimePassword
        };
        var source = NpgsqlDataSource.Create(cs.ConnectionString);
        return (source, new PgSyntheticCostAwarenessJournal(source));
    }

    private static SyntheticCostPreview NewPreview(
        string member = "test:member-one", Guid? id = null, long revision = 1,
        decimal? provisional = null)
    {
        var lines = Enum.GetValues<SyntheticCostComponent>()
            .Select(kind => new SyntheticCostLine(kind,
                SyntheticCostEvidenceState.Missing, null, null, null, Guid.Empty, 0))
            .ToArray();
        if (provisional is not null)
            lines[0] = new(SyntheticCostComponent.ProviderService,
                SyntheticCostEvidenceState.ProvisionalClaim,
                provisional, "IRR", "test:unverified-provider",
                Guid.Parse("99999999-0000-4000-8000-000000000001"), 1);
        return SyntheticCostPreviewBuilder.Build(member, id ?? Guid.NewGuid(), revision, lines);
    }

    private static CostAwarenessCommand Choice(SyntheticCostPreview p,
        long expected = 0, Guid? request = null,
        CostAwarenessChoice choice = CostAwarenessChoice.Deferred) =>
        new(request ?? Guid.NewGuid(), expected, p.MemberRef!, p.PreviewId,
            p.PreviewRevision, p.Fingerprint!, choice);

    private static Task<SyntheticCostRegistration> Register(
        PgSyntheticCostAwarenessJournal store, SyntheticCostPreview p) =>
        store.RegisterAsync(p, TestContext.Current.CancellationToken);

    private static Task<CostAwarenessChange> Record(
        PgSyntheticCostAwarenessJournal store, CostAwarenessCommand cmd) =>
        store.RecordAsync(cmd, TestContext.Current.CancellationToken);

    private static Task<CostAwarenessSnapshot?> Read(
        PgSyntheticCostAwarenessJournal store, Guid id) =>
        store.ReadAsync(id, TestContext.Current.CancellationToken);

    private static async Task<long> Count(Guid id, string table)
    {
        if (table is not ("internal_test_cost_preview"
            or "internal_test_cost_choice_event"
            or "internal_test_cost_choice_audit"))
            throw new ArgumentException("Only exact synthetic table literals.", nameof(table));
        await using var owner = NpgsqlDataSource.Create(AdminConnection);
        await using var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE preview_ref=@preview", cn);
        cmd.Parameters.AddWithValue("preview", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreviewIsImmutableAndReRegisteringExactContentIsIdempotent()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var preview = NewPreview(provisional: 0);
        Assert.Equal(SyntheticCostRegistration.Registered, await Register(store, preview));
        Assert.Equal(SyntheticCostRegistration.Replayed, await Register(store, preview));
        Assert.Equal(0, (await Read(store, preview.PreviewId))!.Revision);
        Assert.Equal(1, await Count(preview.PreviewId, "internal_test_cost_preview"));
        Assert.Equal(0, await Count(preview.PreviewId, "internal_test_cost_choice_event"));
    }

    [Fact]
    public async Task DifferentPreviewVersionCannotRewriteHistoricalQuote()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var original = NewPreview();
        await Register(store, original);
        var later = NewPreview(id: original.PreviewId, revision: 2);
        Assert.Equal(SyntheticCostRegistration.ConflictingSnapshot,
            await Register(store, later));
        Assert.Equal(1, (await Read(store, original.PreviewId))!.PreviewRevision);
        Assert.Equal(SyntheticCostRegistration.Registered,
            await Register(store, NewPreview(revision: 2)));
    }

    [Fact]
    public async Task ClaimedAcknowledgementPersistsButNeverAuthorizesPayment()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        var command = Choice(p, choice: CostAwarenessChoice.AcknowledgedUncertainty);
        Assert.Equal(CostAwarenessOutcome.RecordedUnverified, (await Record(store, command)).Outcome);
        var snapshot = (await Read(store, p.PreviewId))!;
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(CostAwarenessChoice.AcknowledgedUncertainty,
            snapshot.History[0].Command.Choice);
        Assert.Equal(1, await Count(p.PreviewId, "internal_test_cost_choice_event"));
        Assert.Equal(1, await Count(p.PreviewId, "internal_test_cost_choice_audit"));
    }

    [Fact]
    public async Task ExactReplayAfterAnotherChoiceDoesNotAddEvents()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        var first = Choice(p);
        await Record(store, first);
        var later = Choice(p, expected: 1, choice: CostAwarenessChoice.RequestedClarification);
        await Record(store, later);
        Assert.Equal(CostAwarenessOutcome.Replayed, (await Record(store, first)).Outcome);
        Assert.Equal(CostAwarenessOutcome.Replayed, (await Record(store, later)).Outcome);
        Assert.Equal(2, (await Read(store, p.PreviewId))!.Revision);
        Assert.Equal(2, await Count(p.PreviewId, "internal_test_cost_choice_audit"));
    }

    [Fact]
    public async Task ReusedRequestWithAlteredChoiceFailsClosed()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        var command = Choice(p);
        await Record(store, command);
        Assert.Equal(CostAwarenessOutcome.ConflictingReplay,
            (await Record(store, command with
            { Choice = CostAwarenessChoice.AcknowledgedUncertainty })).Outcome);
        Assert.Equal(1, await Count(p.PreviewId, "internal_test_cost_choice_audit"));
    }

    [Fact]
    public async Task ConcurrentSameRequestIsOneAcceptedOneReplay()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        var command = Choice(p);
        var results = await Task.WhenAll(Record(store, command), Record(store, command));
        Assert.Single(results, x => x.Outcome == CostAwarenessOutcome.RecordedUnverified);
        Assert.Single(results, x => x.Outcome == CostAwarenessOutcome.Replayed);
        Assert.Equal(1, await Count(p.PreviewId, "internal_test_cost_choice_audit"));
    }

    [Fact]
    public async Task ConcurrentDistinctRequestsCannotBothUseStaleRevision()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        var results = await Task.WhenAll(Record(store, Choice(p)), Record(store, Choice(p)));
        Assert.Single(results, x => x.Outcome == CostAwarenessOutcome.RecordedUnverified);
        Assert.Single(results, x => x.Outcome == CostAwarenessOutcome.StaleRevision);
        Assert.Equal(1, await Count(p.PreviewId, "internal_test_cost_choice_event"));
    }

    [Fact]
    public async Task ChangedSourceRevisionOrMemberScopeDoesNotAppendChoice()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        Assert.Equal(CostAwarenessOutcome.ScopeMismatch,
            (await Record(store, Choice(p) with { MemberRef = "test:other-member" })).Outcome);
        Assert.Equal(CostAwarenessOutcome.PreviewChanged,
            (await Record(store, Choice(p) with { PreviewRevision = 2 })).Outcome);
        Assert.Equal(CostAwarenessOutcome.PreviewChanged,
            (await Record(store, Choice(p) with { PreviewFingerprint = new string('F', 64) })).Outcome);
        Assert.Equal(0, await Count(p.PreviewId, "internal_test_cost_choice_event"));
    }

    [Fact]
    public async Task NoRegisteredPreviewNeverCreatesAChoiceOrFakeMember()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        Assert.Equal(CostAwarenessOutcome.Invalid, (await Record(store, Choice(p))).Outcome);
        Assert.Null(await Read(store, p.PreviewId));
        Assert.Equal(0, await Count(p.PreviewId, "internal_test_cost_preview"));
    }

    [Fact]
    public async Task MutatedOrRealIdentityPreviewCannotBeRegistered()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        Assert.Equal(SyntheticCostRegistration.Invalid,
            await Register(store, p with { Fingerprint = new string('A', 64) }));
        Assert.Equal(SyntheticCostRegistration.Invalid,
            await Register(store, NewPreview(member: "real:member")));
        Assert.Equal(0, await Count(p.PreviewId, "internal_test_cost_preview"));
    }

    [Fact]
    public async Task AuditInsertFailureRollsBackChoiceAndHeadRevision()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview(member: "test:abort-audit-member");
        await Register(store, p);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Record(store, Choice(p)));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal("TEST_COST_AUDIT_UNAVAILABLE", error.MessageText);
        Assert.Equal(0, (await Read(store, p.PreviewId))!.Revision);
        Assert.Equal(0, await Count(p.PreviewId, "internal_test_cost_choice_event"));
        Assert.Equal(0, await Count(p.PreviewId, "internal_test_cost_choice_audit"));
    }

    [Fact]
    public async Task RestrictedRuntimeCannotDeleteOrAlterPreviewAndHistory()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        await Record(store, Choice(p));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach (var sql in new[]
        {
            "UPDATE internal_test_cost_preview SET member_ref='test:other' WHERE preview_ref=@preview",
            "DELETE FROM internal_test_cost_preview WHERE preview_ref=@preview",
            "UPDATE internal_test_cost_choice_event SET event_digest='FAKE' WHERE preview_ref=@preview",
            "DELETE FROM internal_test_cost_choice_audit WHERE preview_ref=@preview",
            "SELECT count(*) FROM pg_catalog.pg_authid"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("preview", p.PreviewId);
            var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", ex.SqlState);
        }
        Assert.Equal(1, await Count(p.PreviewId, "internal_test_cost_choice_audit"));
    }

    [Fact]
    public async Task PrivilegedDigestTamperIsDetectedBeforeExactReplay()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        var request = Choice(p);
        await Record(store, request);
        await using var owner = NpgsqlDataSource.Create(AdminConnection);
        await using (var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var tx = await cn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await using (var bypass = new NpgsqlCommand(
                "SET LOCAL session_replication_role=replica", cn, tx))
                await bypass.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using (var update = new NpgsqlCommand("""
                UPDATE internal_test_cost_choice_event SET event_digest='NOT-VALID'
                WHERE preview_ref=@preview
                """, cn, tx))
            {
                update.Parameters.AddWithValue("preview", p.PreviewId);
                await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Record(store, request));
        Assert.Equal("TEST_COST_AWARENESS_JOURNAL_CORRUPT", ex.Message);
    }

    [Fact]
    public async Task MissingPairedAuditIsDetectedOnRead()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var p = NewPreview();
        await Register(store, p);
        await Record(store, Choice(p));
        await using var owner = NpgsqlDataSource.Create(AdminConnection);
        await using (var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var tx = await cn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await using (var bypass = new NpgsqlCommand(
                "SET LOCAL session_replication_role=replica", cn, tx))
                await bypass.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using (var remove = new NpgsqlCommand("""
                DELETE FROM internal_test_cost_choice_audit WHERE preview_ref=@preview
                """, cn, tx))
            {
                remove.Parameters.AddWithValue("preview", p.PreviewId);
                await remove.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(store, p.PreviewId));
        Assert.Equal("TEST_COST_AWARENESS_JOURNAL_CORRUPT", ex.Message);
    }
}
