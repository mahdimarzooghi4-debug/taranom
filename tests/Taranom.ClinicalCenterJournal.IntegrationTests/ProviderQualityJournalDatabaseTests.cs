using Npgsql;
using Taranom.ClinicalCenterGovernance;
using Taranom.ClinicalCenterJournal.Postgres;
using Xunit;

namespace Taranom.ClinicalCenterJournal.IntegrationTests;

// Actual isolated PostgreSQL; NEVER authorized provider onboarding,
// quality signoff, incident report or clinical service activation.
public sealed class ProviderQualityJournalDatabaseTests
{
    private static readonly Lazy<Task> Ready = new(BootstrapAsync);
    private const string User = "taranom_sprint110_quality_test";
    private const string Password = "only_ephemeral_quality_test";
    private static string AdminConnection =>
        Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ?? throw new InvalidOperationException("TARANOM_TEST_PG required");

    private static async Task BootstrapAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var owner = NpgsqlDataSource.Create(AdminConnection);
        await PgSyntheticProviderQualityJournal.InitializeIsolatedTestSchemaAsync(owner,ct);
        await using var cn = await owner.OpenConnectionAsync(ct);
        await using var grants = new NpgsqlCommand("""
            DO $role$ BEGIN
              IF NOT EXISTS(SELECT 1 FROM pg_roles
                WHERE rolname='taranom_sprint110_quality_test') THEN
                CREATE ROLE taranom_sprint110_quality_test
                  LOGIN PASSWORD 'only_ephemeral_quality_test';
              END IF;
            END $role$;
            REVOKE ALL ON internal_test_provider_quality_case,
              internal_test_provider_quality_event, internal_test_provider_quality_audit
              FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO taranom_sprint110_quality_test;
            GRANT SELECT,INSERT,UPDATE ON internal_test_provider_quality_case
              TO taranom_sprint110_quality_test;
            GRANT SELECT,INSERT ON internal_test_provider_quality_event,
              internal_test_provider_quality_audit
              TO taranom_sprint110_quality_test;
            """, cn);
        await grants.ExecuteNonQueryAsync(ct);
    }
    private static async Task<(NpgsqlDataSource Data,PgSyntheticProviderQualityJournal Store)> Open()
    {
        await Ready.Value.WaitAsync(TestContext.Current.CancellationToken);
        var cs = new NpgsqlConnectionStringBuilder(AdminConnection)
        {
            Username=User,Password=Password
        };
        var db=NpgsqlDataSource.Create(cs.ConnectionString);
        return (db,new PgSyntheticProviderQualityJournal(db));
    }
    private static string Case()=> "test:provider-quality-"+Guid.NewGuid().ToString("N");
    private static SyntheticProviderQualityNote Root(string c,
        SyntheticProviderPath path=SyntheticProviderPath.DirectContract)=>new(
        Guid.NewGuid(),0,SyntheticProviderQualityNoteAction.RecordUnverifiedQualityScope,
        c,"test:tenant-A","test:provider-A","test:service-A",
        path,path==SyntheticProviderPath.QualifiedPartnerNetwork?"test:network-A":null,
        Guid.Empty,null,null,null,null);
    private static SyntheticProviderQualityNote Note(SyntheticProviderQualityNote root,
        SyntheticProviderQualityNoteAction action,long version=1)=>new(
        Guid.NewGuid(),version,action,root.CaseRef,root.TenantRef,
        root.ProviderRef,root.ServiceRef,root.Path,root.PartnerNetworkRef,
        root.RequestId,"test:unverified-evidence","test:claimed-actor",
        action==SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference
            ?"test:unverified-incident":null,
        action==SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference
            ?"test:unverified-recipient":null);
    private static Task<SyntheticProviderQualityChange> Apply(
        PgSyntheticProviderQualityJournal store,string c,SyntheticProviderQualityNote n)=>
        store.ApplyAsync(c,n,TestContext.Current.CancellationToken);
    private static Task<SyntheticProviderQualitySnapshot> Read(
        PgSyntheticProviderQualityJournal store,string c)=>
        store.ReadAsync(c,TestContext.Current.CancellationToken);
    private static async Task<long> Count(string table,string c)
    {
        if(table is not("internal_test_provider_quality_case" or
            "internal_test_provider_quality_event" or "internal_test_provider_quality_audit"))
            throw new ArgumentException("Unsupported test table",nameof(table));
        await using var db=NpgsqlDataSource.Create(AdminConnection);
        await using var cn=await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd=new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE case_ref=@case",cn);
        cmd.Parameters.AddWithValue("case",c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DirectProviderThreeClaimedNotesSurviveAuditedRead()
    {
        var (db,store)=await Open();await using var _=db;
        var c=Case();var root=Root(c);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.RecordedUnverified,
            (await Apply(store,c,root)).Outcome);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.RecordedUnverified,
            (await Apply(store,c,Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote))).Outcome);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.RecordedUnverified,
            (await Apply(store,c,Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference,2))).Outcome);
        var state=await Read(store,c);
        Assert.Equal(3,state.Revision);
        Assert.Equal(3,await Count("internal_test_provider_quality_event",c));
        Assert.Equal(3,await Count("internal_test_provider_quality_audit",c));
        var counts=await store.ReadUnverifiedCountsAsync(c,TestContext.Current.CancellationToken);
        Assert.NotNull(counts);
        Assert.Equal(1,counts.Value.ClaimedTaranomStandardNoteCount);
        Assert.Equal(0,counts.Value.ClaimedNetworkControlNoteCount);
        Assert.Equal(1,counts.Value.ClaimedIncidentHandoffReferenceCount);
    }

    [Fact]
    public async Task NetworkProviderNeedsOwnNetworkScopeAndNote()
    {
        var (db,store)=await Open();await using var _=db;
        var c=Case();var root=Root(c,SyntheticProviderPath.QualifiedPartnerNetwork);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.RecordedUnverified,
            (await Apply(store,c,root)).Outcome);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.RecordedUnverified,
            (await Apply(store,c,Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedNetworkProviderControlNote))).Outcome);
        var result=await store.ReadUnverifiedCountsAsync(c,TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(SyntheticProviderPath.QualifiedPartnerNetwork,result.Value.Path);
        Assert.Equal(1,result.Value.ClaimedNetworkControlNoteCount);
        Assert.Equal(2,result.Value.Revision);
        var bad=Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote,2)
            with{PartnerNetworkRef="test:different-network"};
        Assert.Equal(SyntheticProviderQualityNoteOutcome.ScopeMismatch,
            (await Apply(store,c,bad)).Outcome);
        Assert.Equal(2,(await Read(store,c)).Revision);
    }

    [Fact]
    public async Task ExactOldReplayAndChangedSameUuidNeverDoubleAppend()
    {
        var (db,store)=await Open();await using var _=db;
        var c=Case();var root=Root(c);
        var note=Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote);
        await Apply(store,c,root);await Apply(store,c,note);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.Replayed,
            (await Apply(store,c,root)).Outcome);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.Replayed,
            (await Apply(store,c,note)).Outcome);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.ConflictingReplay,
            (await Apply(store,c,note with{UnverifiedEvidenceRef="test:changed"})).Outcome);
        Assert.Equal(2,await Count("internal_test_provider_quality_audit",c));
    }

    [Fact]
    public async Task RejectedNetworkOrAlteredEvidenceNeverMutates()
    {
        var (db,store)=await Open();await using var _=db;
        var c=Case();var root=Root(c);await Apply(store,c,root);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.NetworkScopeDenied,
            (await Apply(store,c,Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedNetworkProviderControlNote))).Outcome);
        var forged=Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference)
            with{OriginalScopeRequestId=Guid.NewGuid()};
        Assert.Equal(SyntheticProviderQualityNoteOutcome.ScopeMismatch,
            (await Apply(store,c,forged)).Outcome);
        Assert.Equal(SyntheticProviderQualityNoteOutcome.Invalid,
            (await Apply(store,c,Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference)
                with{UnverifiedIncidentRef=null})).Outcome);
        var emptyCase=Case();
        Assert.Equal(SyntheticProviderQualityNoteOutcome.OriginalScopeMissing,
            (await Apply(store,emptyCase,Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote)
                with{CaseRef=emptyCase})).Outcome);
        Assert.Equal(1,(await Read(store,c)).Revision);
    }

    [Fact]
    public async Task ConcurrentWritersOneAcceptAndOtherStale()
    {
        var (db,store)=await Open();await using var _=db;
        var c=Case();var root=Root(c);await Apply(store,c,root);
        var results=await Task.WhenAll(
            Apply(store,c,Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedTaranomStandardNote)),
            Apply(store,c,Note(root,SyntheticProviderQualityNoteAction.RecordUnverifiedIncidentHandoffReference)));
        Assert.Single(results,x=>x.Outcome==SyntheticProviderQualityNoteOutcome.RecordedUnverified);
        Assert.Single(results,x=>x.Outcome==SyntheticProviderQualityNoteOutcome.StaleRevision);
        Assert.Equal(2,await Count("internal_test_provider_quality_audit",c));
    }

    [Fact]
    public async Task ForgedHeaderCausesReadAndReplayToFailClosed()
    {
        var (db,store)=await Open();await using var _=db;
        var c=Case();var root=Root(c);await Apply(store,c,root);
        await using var admin=NpgsqlDataSource.Create(AdminConnection);
        await using(var cn=await admin.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using(var update=new NpgsqlCommand("""
            UPDATE internal_test_provider_quality_case SET current_revision=2
            WHERE case_ref=@case
            """,cn))
        {
            update.Parameters.AddWithValue("case",c);
            Assert.Equal(1,await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Read(store,c));
        Assert.Equal("TEST_PROVIDER_QUALITY_SOURCE_UNVERIFIABLE",ex.Message);
        var replay=await Assert.ThrowsAsync<InvalidOperationException>(()=>Apply(store,c,root));
        Assert.Equal("TEST_PROVIDER_QUALITY_SOURCE_UNVERIFIABLE",replay.Message);
    }

    [Fact]
    public async Task RuntimeCannotRewriteEventOrAuditAndMissingCaseRemainsEmpty()
    {
        var (db,store)=await Open();await using var _=db;
        var c=Case();await Apply(store,c,Root(c));
        await using var cn=await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach(var sql in new[]{
            "UPDATE internal_test_provider_quality_event SET action_code=4 WHERE case_ref=@case",
            "DELETE FROM internal_test_provider_quality_audit WHERE case_ref=@case"})
        {
            await using var cmd=new NpgsqlCommand(sql,cn);
            cmd.Parameters.AddWithValue("case",c);
            var exception=await Assert.ThrowsAsync<PostgresException>(
                async()=>await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501",exception.SqlState);
        }
        var absent=Case();
        Assert.Equal(0,(await Read(store,absent)).Revision);
        Assert.Equal(0,await Count("internal_test_provider_quality_case",absent));
        Assert.Null(await store.ReadUnverifiedCountsAsync(absent,TestContext.Current.CancellationToken));
    }
}
