using Npgsql;
using Taranom.MemberGovernance;
using Taranom.ProofingJournal.Postgres;
using Xunit;

namespace Taranom.ProofingJournal.IntegrationTests;

// Synthetic refs and ephemeral CI-only credentials. No real income, patients,
// financial balance or production evidence archive is ever connected here.
public sealed class LateEvidenceDatabaseTests
{
    private static readonly Lazy<Task> Ready = new(PrepareAsync);

    private static string AdminConnection =>
        Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("Isolated TARANOM_TEST_PG required.");

    private static async Task PrepareAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var owner = NpgsqlDataSource.Create(AdminConnection);
        await PgLateEvidenceWorkItemJournal.InitializeIsolatedTestSchemaAsync(owner, ct);
        await using var cn = await owner.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            DO $block$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles
                WHERE rolname='taranom_sprint27_late_evidence_test') THEN
                CREATE ROLE taranom_sprint27_late_evidence_test LOGIN
                  PASSWORD 'only_ephemeral_late_evidence_test';
              END IF;
            END $block$;
            REVOKE ALL ON internal_test_late_evidence_case,
                internal_test_late_evidence_event,
                internal_test_late_evidence_audit FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO taranom_sprint27_late_evidence_test;
            GRANT SELECT,INSERT,UPDATE ON internal_test_late_evidence_case
                TO taranom_sprint27_late_evidence_test;
            GRANT SELECT,INSERT ON internal_test_late_evidence_event,
                internal_test_late_evidence_audit
                TO taranom_sprint27_late_evidence_test;
            """, cn);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(NpgsqlDataSource Db, PgLateEvidenceWorkItemJournal Store)>
        ConnectAsync()
    {
        await Ready.Value.WaitAsync(TestContext.Current.CancellationToken);
        var config = new NpgsqlConnectionStringBuilder(AdminConnection)
        {
            Username = "taranom_sprint27_late_evidence_test",
            Password = "only_ephemeral_late_evidence_test"
        };
        var source = NpgsqlDataSource.Create(config.ConnectionString);
        return (source, new PgLateEvidenceWorkItemJournal(source));
    }

    private static LateEvidenceCommand Submit(string caseRef,
        Guid? request = null) =>
        new(request ?? Guid.NewGuid(), 0,
            LateEvidenceAction.SubmitUnverifiedEvidence,
            caseRef, "test:member-a", 
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero),
            "test:opaque-unverified-benefit-document",
            "test:claimed-submitter", Guid.Empty, null, null);

    private static LateEvidenceCommand Note(
        LateEvidenceCommand origin, long expectedRevision = 1,
        Guid? request = null) =>
        origin with
        {
            Action = LateEvidenceAction.RecordUnverifiedHumanNote,
            ExpectedRevision = expectedRevision,
            RequestId = request ?? Guid.NewGuid(),
            OriginalSubmissionRequestId = origin.RequestId,
            ReviewerClaimRef = "test:claimed-reviewer",
            ReviewNoteEvidenceRef = "test:unverified-human-review-note"
        };

    private static string NewCase(string kind = "case") =>
        "test:" + kind + "-" + Guid.NewGuid().ToString("N");

    private static Task<LateEvidenceChange> Apply(
        PgLateEvidenceWorkItemJournal store, LateEvidenceCommand input) =>
        store.ApplyAsync(input, TestContext.Current.CancellationToken);

    private static Task<LateEvidenceCaseSnapshot> Read(
        PgLateEvidenceWorkItemJournal store, string caseRef) =>
        store.ReadAsync(caseRef, TestContext.Current.CancellationToken);

    private static async Task<long> Count(string caseRef, string table)
    {
        if (table is not ("internal_test_late_evidence_case"
            or "internal_test_late_evidence_event"
            or "internal_test_late_evidence_audit"))
            throw new ArgumentException("Only three fixture table literals.", nameof(table));
        await using var owner = NpgsqlDataSource.Create(AdminConnection);
        await using var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var sql = new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE case_ref=@case", cn);
        sql.Parameters.AddWithValue("case", caseRef);
        return (long)(await sql.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task SubmittedCaseAndHumanNotePersistWithPairedAppendOnlyAudit()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase();
        var source = Submit(id);
        Assert.Equal(LateEvidenceOutcome.RecordedUnverified,
            (await Apply(store, source)).Outcome);
        Assert.Equal(LateEvidenceOutcome.RecordedUnverified,
            (await Apply(store, Note(source))).Outcome);
        var state = await Read(store, id);
        Assert.Equal(2, state.Revision);
        Assert.Equal(2, state.Events.Length);
        Assert.Equal(2, await Count(id, "internal_test_late_evidence_event"));
        Assert.Equal(2, await Count(id, "internal_test_late_evidence_audit"));
        Assert.Equal(LateEvidenceAction.RecordUnverifiedHumanNote,
            state.Events[1].Command.Action);
    }

    [Fact]
    public async Task ReplayAfterNoteKeepsCurrentHistoryAndDoesNotDuplicateAudit()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase();
        var source = Submit(id);
        await Apply(store, source);
        var note = Note(source);
        await Apply(store, note);
        Assert.Equal(LateEvidenceOutcome.Replayed, (await Apply(store, source)).Outcome);
        Assert.Equal(LateEvidenceOutcome.Replayed, (await Apply(store, note)).Outcome);
        Assert.Equal(2, (await Read(store, id)).Revision);
        Assert.Equal(2, await Count(id, "internal_test_late_evidence_audit"));
    }

    [Fact]
    public async Task ChangedReplayIsRejectedWithoutWritingNewEvent()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase();
        var first = Submit(id);
        await Apply(store, first);
        Assert.Equal(LateEvidenceOutcome.ConflictingReplay,
            (await Apply(store, first with { EvidenceRef = "test:tampered" })).Outcome);
        Assert.Equal(1, await Count(id, "internal_test_late_evidence_event"));
    }

    [Fact]
    public async Task ConcurrentDifferentRequestsSerializeToOneAcceptedCase()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase();
        var a = Submit(id);
        var b = Submit(id);
        var outputs = await Task.WhenAll(Apply(store, a), Apply(store, b));
        Assert.Single(outputs, x => x.Outcome == LateEvidenceOutcome.RecordedUnverified);
        Assert.Single(outputs, x => x.Outcome == LateEvidenceOutcome.StaleRevision);
        Assert.Equal(1, await Count(id, "internal_test_late_evidence_audit"));
    }

    [Fact]
    public async Task ConcurrentExactRequestAllowsOnlyOneAcceptedAndOneReplay()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase();
        var command = Submit(id);
        var outputs = await Task.WhenAll(Apply(store, command), Apply(store, command));
        Assert.Single(outputs, x => x.Outcome == LateEvidenceOutcome.RecordedUnverified);
        Assert.Single(outputs, x => x.Outcome == LateEvidenceOutcome.Replayed);
        Assert.Equal(1, await Count(id, "internal_test_late_evidence_audit"));
    }

    [Fact]
    public async Task ReviewerCannotRewriteSubmittedMemberOrPeriod()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase();
        var original = Submit(id);
        await Apply(store, original);
        var forged = Note(original) with { MemberRef = "test:other-member" };
        Assert.Equal(LateEvidenceOutcome.ScopeMismatch,
            (await Apply(store, forged)).Outcome);
        Assert.Equal(1, (await Read(store, id)).Revision);
    }

    [Fact]
    public async Task AuditInsertFailureRollsBackCaseAndSubmission()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase("abort-note-audit");
        var error = await Assert.ThrowsAsync<PostgresException>(
            () => Apply(store, Submit(id)));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal("TEST_LATE_EVIDENCE_AUDIT_UNAVAILABLE", error.MessageText);
        Assert.Equal(0, await Count(id, "internal_test_late_evidence_case"));
        Assert.Equal(0, await Count(id, "internal_test_late_evidence_event"));
    }

    [Fact]
    public async Task RuntimeRoleCannotEditHistoryOrReadRestrictedCatalog()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase();
        await Apply(store, Submit(id));
        await using var cn = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach (var sql in new[]
        {
            "UPDATE internal_test_late_evidence_event SET action_code=2 WHERE case_ref=@case",
            "DELETE FROM internal_test_late_evidence_audit WHERE case_ref=@case",
            "SELECT count(*) FROM pg_catalog.pg_authid"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("case", id);
            var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", ex.SqlState);
        }
        Assert.Equal(1, await Count(id, "internal_test_late_evidence_audit"));
    }

    [Fact]
    public async Task CorruptedHistoricalDigestFailsBeforeAnExactReplayIsReturned()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var id = NewCase();
        var source = Submit(id);
        await Apply(store, source);
        await using var owner = NpgsqlDataSource.Create(AdminConnection);
        await using (var cn = await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var tx = await cn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await using (var bypass = new NpgsqlCommand(
                "SET LOCAL session_replication_role=replica", cn, tx))
                await bypass.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using (var forged = new NpgsqlCommand("""
                UPDATE internal_test_late_evidence_event
                SET event_digest='NOT-THE-ORIGINAL-DIGEST' WHERE case_ref=@case
                """, cn, tx))
            {
                forged.Parameters.AddWithValue("case", id);
                await forged.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Apply(store, source));
        Assert.Equal("TEST_LATE_EVIDENCE_JOURNAL_CORRUPT", ex.Message);
    }

    [Fact]
    public async Task RealIdentifierNeverCreatesCaseOrEvidenceRow()
    {
        var (db, store) = await ConnectAsync(); await using var _ = db;
        var input = Submit("real:medical-membership");
        Assert.Equal(LateEvidenceOutcome.Invalid, (await Apply(store, input)).Outcome);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Read(store, input.CaseRef));
    }
}
