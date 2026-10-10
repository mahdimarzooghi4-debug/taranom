using Npgsql;
using Taranom.MemberGovernance;
using Taranom.ProofingJournal.Postgres;
using Xunit;

namespace Taranom.ProofingJournal.IntegrationTests;

// Genuine PostgreSQL tests against an ephemeral *synthetic-only* CI database.
// Every case key is freshly generated; credentials exist only in the CI fixture.
public sealed class ProofingJournalDatabaseTests
{
    private static readonly Lazy<Task> Ready = new(BootstrapAsync);
    private const string RuntimeUser = "taranom_sprint03_journal_test";
    private const string RuntimePassword = "only_ephemeral_proofing_journal_test";

    private static string AdminConnection => Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG required for isolated CI PostgreSQL");

    private static async Task BootstrapAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await PgProofingCaseJournal.InitializeIsolatedTestSchemaAsync(admin, ct);
        await using var cn = await admin.OpenConnectionAsync(ct);
        await using var grant = new NpgsqlCommand("""
            DO $role$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='taranom_sprint03_journal_test') THEN
                    CREATE ROLE taranom_sprint03_journal_test
                        LOGIN PASSWORD 'only_ephemeral_proofing_journal_test';
                END IF;
            END;
            $role$;
            REVOKE ALL ON internal_test_proofing_case,
              internal_test_proofing_event, internal_test_proofing_audit FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO taranom_sprint03_journal_test;
            GRANT SELECT, INSERT, UPDATE ON internal_test_proofing_case
                TO taranom_sprint03_journal_test;
            GRANT SELECT, INSERT ON internal_test_proofing_event,
                internal_test_proofing_audit TO taranom_sprint03_journal_test;
            """, cn);
        await grant.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(NpgsqlDataSource Db, PgProofingCaseJournal Store)> ConnectAsync()
    {
        await Ready.Value.WaitAsync(TestContext.Current.CancellationToken);
        var cs = new NpgsqlConnectionStringBuilder(AdminConnection)
        {
            Username = RuntimeUser,
            Password = RuntimePassword
        };
        var db = NpgsqlDataSource.Create(cs.ConnectionString);
        return (db, new PgProofingCaseJournal(db));
    }

    private static string NewCase(string prefix = "case") =>
        "test:" + prefix + "-" + Guid.NewGuid().ToString("N");

    private static ProofingCaseCommand Observe(
        Guid request, long expectedCase, long expectedIdentity, ProofingObservation result) =>
        new(request, expectedCase, expectedIdentity, ProofingCaseAction.ObserveProofing,
            new ProofingReviewInput(result, EvidenceCheck.Confirmed, EvidenceCheck.Confirmed,
                EvidenceCheck.Confirmed, EvidenceCheck.Confirmed, false, false),
            EvidenceCheck.Missing, false);

    private static ProofingCaseCommand Correct(Guid request, long expectedCase,
        long expectedIdentity) => new(request, expectedCase, expectedIdentity,
            ProofingCaseAction.ApplicantCorrectedIdentity, default, EvidenceCheck.Confirmed, false);

    private static async Task<long> CountAsync(
        NpgsqlDataSource db, string table, string key)
    {
        // The table name is selected exclusively from these test-owned literals.
        if (table is not ("internal_test_proofing_event" or
                          "internal_test_proofing_audit" or "internal_test_proofing_case"))
            throw new ArgumentException("Invalid test table", nameof(table));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE case_ref=@case", cn);
        cmd.Parameters.AddWithValue("case", key);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    // Every I/O path propagates xUnit's test cancellation signal.
    private static Task<ProofingCaseChange> ApplyAsync(
        PgProofingCaseJournal store, string caseRef, ProofingCaseCommand input) =>
        store.ApplyAsync(caseRef, input, TestContext.Current.CancellationToken);

    private static Task<ProofingCaseSnapshot> ReadAsync(
        PgProofingCaseJournal store, string caseRef) =>
        store.ReadAsync(caseRef, TestContext.Current.CancellationToken);

    [Fact]
    public async Task DurableWriteRebuildsFromEventAndMatchingAudit()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        var first = await ApplyAsync(store, key, Observe(Guid.NewGuid(), 0, 0,
            ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.Applied, first.Outcome);
        var rebuilt = await ReadAsync(store, key);
        Assert.Equal(1L, rebuilt.CaseRevision);
        Assert.Equal(ProofingReviewState.ValidatedProofingEvidenceOnly, rebuilt.Review);
        Assert.Equal(1L, await CountAsync(db, "internal_test_proofing_event", key));
        Assert.Equal(1L, await CountAsync(db, "internal_test_proofing_audit", key));
    }

    [Fact]
    public async Task ExactHistoricalReplayDoesNotDuplicateAudit()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        var original = Observe(Guid.NewGuid(), 0, 0, ProofingObservation.DefinitiveMismatch);
        var first = await ApplyAsync(store, key, original);
        Assert.Equal(ProofingJournalOutcome.Applied, first.Outcome);
        var corrected = await ApplyAsync(store, key, Correct(Guid.NewGuid(), 1, 0));
        Assert.Equal(ProofingJournalOutcome.Applied, corrected.Outcome);
        var replay = await ApplyAsync(store, key, original);
        Assert.Equal(ProofingJournalOutcome.Replayed, replay.Outcome);
        Assert.Equal(2L, replay.Snapshot.CaseRevision);
        Assert.Equal(2L, await CountAsync(db, "internal_test_proofing_event", key));
        Assert.Equal(2L, await CountAsync(db, "internal_test_proofing_audit", key));
    }

    [Fact]
    public async Task SameRequestChangedPayloadFailsWithoutMutatingHistory()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        var original = Observe(Guid.NewGuid(), 0, 0, ProofingObservation.ProviderUnavailable);
        Assert.Equal(ProofingJournalOutcome.Applied,
            (await ApplyAsync(store, key, original)).Outcome);
        var mutated = original with { ProofingEvidence = original.ProofingEvidence with
            { Observation = ProofingObservation.VerifiedMatch } };
        Assert.Equal(ProofingJournalOutcome.ConflictingReplay,
            (await ApplyAsync(store, key, mutated)).Outcome);
        Assert.Equal(1L, await CountAsync(db, "internal_test_proofing_audit", key));
    }

    [Fact]
    public async Task ConcurrentDifferentRequestsHaveOnlyOneAppliedChange()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        var operations = new[]
        {
            Observe(Guid.NewGuid(), 0, 0, ProofingObservation.ProviderUnavailable),
            Observe(Guid.NewGuid(), 0, 0, ProofingObservation.VerifiedMatch)
        };
        var results = await Task.WhenAll(operations.Select(x => ApplyAsync(store, key, x)));
        Assert.Single(results, x => x.Outcome == ProofingJournalOutcome.Applied);
        Assert.Single(results, x => x.Outcome == ProofingJournalOutcome.StaleCaseRevision);
        Assert.Equal(1L, await CountAsync(db, "internal_test_proofing_event", key));
        Assert.Equal(1L, await CountAsync(db, "internal_test_proofing_audit", key));
    }

    [Fact]
    public async Task ConcurrentSameRequestSerializesToOneAcceptedOneReplay()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        var request = Observe(Guid.NewGuid(), 0, 0, ProofingObservation.Inconclusive);
        var outputs = await Task.WhenAll(ApplyAsync(store, key, request),
            ApplyAsync(store, key, request));
        Assert.Single(outputs, x => x.Outcome == ProofingJournalOutcome.Applied);
        Assert.Single(outputs, x => x.Outcome == ProofingJournalOutcome.Replayed);
        Assert.Equal(1L, await CountAsync(db, "internal_test_proofing_audit", key));
    }

    [Fact]
    public async Task IdentityRevisionRemainsIsolatedAfterCorrection()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        await ApplyAsync(store, key, Observe(Guid.NewGuid(), 0, 0,
            ProofingObservation.DefinitiveMismatch));
        await ApplyAsync(store, key, Correct(Guid.NewGuid(), 1, 0));
        var old = await ApplyAsync(store, key, Observe(Guid.NewGuid(), 2, 0,
            ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.StaleIdentityRevision, old.Outcome);
        var next = await ApplyAsync(store, key, Observe(Guid.NewGuid(), 2, 1,
            ProofingObservation.VerifiedMatch));
        Assert.Equal(ProofingJournalOutcome.Applied, next.Outcome);
        Assert.Equal(3L, (await ReadAsync(store, key)).CaseRevision);
        Assert.Equal(3L, await CountAsync(db, "internal_test_proofing_audit", key));
    }

    [Fact]
    public async Task AuditWriteFailureRollsBackStateAndEventTogether()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase("abort");
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyAsync(store, key, Observe(Guid.NewGuid(), 0, 0,
                ProofingObservation.VerifiedMatch)));
        Assert.Equal("P0001", exception.SqlState);
        Assert.Equal("TEST_PROOFING_AUDIT_UNAVAILABLE", exception.MessageText);
        Assert.Equal(0L, await CountAsync(db, "internal_test_proofing_case", key));
        Assert.Equal(0L, await CountAsync(db, "internal_test_proofing_event", key));
        Assert.Equal(0L, await CountAsync(db, "internal_test_proofing_audit", key));
    }

    [Fact]
    public async Task RuntimeCannotUpdateOrDeleteEventHistory()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        await ApplyAsync(store, key, Observe(Guid.NewGuid(), 0, 0,
            ProofingObservation.VerifiedMatch));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using (var update = new NpgsqlCommand("""
            UPDATE internal_test_proofing_event SET review_code=0 WHERE case_ref=@case
            """, cn))
        {
            update.Parameters.AddWithValue("case", key);
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", error.SqlState);
        }
        await using (var deletion = new NpgsqlCommand("""
            DELETE FROM internal_test_proofing_audit WHERE case_ref=@case
            """, cn))
        {
            deletion.Parameters.AddWithValue("case", key);
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await deletion.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", error.SqlState);
        }
    }

    [Fact]
    public async Task SchemaOwnerCannotSilentlyRewriteHistoryThroughNormalDml()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        await ApplyAsync(store, key, Observe(Guid.NewGuid(), 0, 0,
            ProofingObservation.VerifiedMatch));
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await using var cn = await admin.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM internal_test_proofing_event WHERE case_ref=@case
            """, cn);
        cmd.Parameters.AddWithValue("case", key);
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("TEST_PROOFING_APPEND_ONLY", error.MessageText);
        Assert.Equal(1L, await CountAsync(db, "internal_test_proofing_event", key));
    }

    [Fact]
    public async Task ProjectionCorruptionFailsClosedOnReadAndWrite()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var key = NewCase();
        await ApplyAsync(store, key, Observe(Guid.NewGuid(), 0, 0,
            ProofingObservation.VerifiedMatch));
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await using (var cn = await admin.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var cmd = new NpgsqlCommand("""
            UPDATE internal_test_proofing_case SET case_revision=55 WHERE case_ref=@case
            """, cn))
        {
            cmd.Parameters.AddWithValue("case", key);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var read = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(store, key));
        Assert.Equal("TEST_PROOFING_JOURNAL_CORRUPT", read.Message);
        var write = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApplyAsync(store, key, Observe(Guid.NewGuid(), 1, 0,
                ProofingObservation.VerifiedMatch)));
        Assert.Equal("TEST_PROOFING_JOURNAL_CORRUPT", write.Message);
        Assert.Equal(1L, await CountAsync(db, "internal_test_proofing_audit", key));
    }

    [Fact]
    public async Task UnconnectedOrPersonalDataReferencesAreRejected()
    {
        var (db, store) = await ConnectAsync();
        await using var _ = db;
        var cmd = Observe(Guid.NewGuid(), 0, 0, ProofingObservation.VerifiedMatch);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApplyAsync(store, "actual-person-identifier", cmd));
        Assert.Equal("TEST_PROOFING_CASE_REF_REQUIRED", error.Message);
        Assert.Equal(0L, (await ReadAsync(store, NewCase())).CaseRevision);
    }
}
