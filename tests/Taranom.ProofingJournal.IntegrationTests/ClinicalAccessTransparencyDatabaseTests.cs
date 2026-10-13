using Npgsql;
using Taranom.MemberGovernance;
using Taranom.ProofingJournal.Postgres;
using Xunit;

namespace Taranom.ProofingJournal.IntegrationTests;

// Real isolated PostgreSQL only. NEVER a production consent, patient or
// clinical-access test. All principal/provider identifiers are untrusted test:.
[Collection("Proofing isolated PostgreSQL schema")]
public sealed class ClinicalAccessTransparencyDatabaseTests
{
    private static readonly Lazy<Task> Ready=new(SetupAsync);
    private static string Connection=>Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
        ??throw new InvalidOperationException("Isolated TARANOM_TEST_PG required");
    private static async Task SetupAsync()
    {
        await using var owner=NpgsqlDataSource.Create(Connection);
        await PgSyntheticClinicalAccessTransparencyJournal
            .InitializeIsolatedTestSchemaAsync(owner,TestContext.Current.CancellationToken);
        await using var cn=await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd=new NpgsqlCommand("""
            DO $block$ BEGIN
             IF NOT EXISTS (SELECT 1 FROM pg_roles
              WHERE rolname='taranom_sprint109_access_test') THEN
              CREATE ROLE taranom_sprint109_access_test LOGIN
               PASSWORD 'only_ephemeral_clinical_access_test';
             END IF;
            END $block$;
            REVOKE ALL ON internal_test_clinical_access_case,
              internal_test_clinical_access_event,
              internal_test_clinical_access_audit FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO taranom_sprint109_access_test;
            GRANT SELECT,INSERT,UPDATE ON internal_test_clinical_access_case
              TO taranom_sprint109_access_test;
            GRANT SELECT,INSERT ON internal_test_clinical_access_event,
              internal_test_clinical_access_audit
              TO taranom_sprint109_access_test;
            """,cn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
    private static async Task<(NpgsqlDataSource Source,
        PgSyntheticClinicalAccessTransparencyJournal Store)> OpenAsync()
    {
        await Ready.Value;
        var cs=new NpgsqlConnectionStringBuilder(Connection)
        {
            Username="taranom_sprint109_access_test",
            Password="only_ephemeral_clinical_access_test"
        };
        var source=NpgsqlDataSource.Create(cs.ConnectionString);
        return(source,new PgSyntheticClinicalAccessTransparencyJournal(source));
    }
    private static string Case()=> "test:access-"+Guid.NewGuid().ToString("N");
    private static SyntheticRecordDisclosureInput Scope=>new(
        SyntheticRecordRequester.Clinician,
        SyntheticRecordPurpose.ClinicianTreatmentNeed,
        "test:tenant-A","test:tenant-A",
        "test:member-A","test:member-A","test:member-A",
        "test:clinician-A","test:clinician-A","test:clinician-A",
        "test:care-service-A","test:care-service-A",
        "test:category-A","test:category-A",
        "test:clinical-need-A","test:record-source-A",
        1,1,1,
        EvidenceCheck.Confirmed,EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,EvidenceCheck.Confirmed);
    private static SelfReadBoundaryInput Viewer=>new(
        SelfReadActorKind.Member,"test:member-A","test:tenant-A",
        "test:member-A","test:tenant-A",null,null,
        EvidenceCheck.Confirmed,EvidenceCheck.Confirmed,
        EvidenceCheck.Confirmed,
        new ReadPrerequisiteSnapshot(
            EvidenceCheck.Confirmed,EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed,EvidenceCheck.Confirmed,
            EvidenceCheck.Confirmed,EvidenceCheck.Confirmed));
    private static SyntheticAccessNoteCommand Root(Guid? id=null)=>new(
        id??Guid.NewGuid(),0,SyntheticAccessNoteAction.OpenUnverifiedClinicianRequest,
        Scope,Guid.Empty,null,null);
    private static SyntheticAccessNoteCommand Concern(
        SyntheticAccessNoteCommand origin,long version=1)=>new(
        Guid.NewGuid(),version,SyntheticAccessNoteAction.RecordUnverifiedMemberConcern,
        origin.ClaimedDisclosureScope,origin.RequestId,
        origin.ClaimedDisclosureScope.MemberRef,"test:unverified-member-concern");
    private static SyntheticAccessNoteCommand Oversight(
        SyntheticAccessNoteCommand origin,long version=1)=>new(
        Guid.NewGuid(),version,SyntheticAccessNoteAction.RecordUnverifiedOversightNote,
        origin.ClaimedDisclosureScope,origin.RequestId,
        "test:separate-oversight-claim","test:unverified-oversight-note");
    private static async Task<long> CountAsync(string table,string caseRef)
    {
        if(table is not ("internal_test_clinical_access_case" or
            "internal_test_clinical_access_event" or
            "internal_test_clinical_access_audit"))
            throw new ArgumentException("Unknown synthetic table");
        await using var owner=NpgsqlDataSource.Create(Connection);
        await using var cn=await owner.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var query=new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE case_ref=@case",cn);
        query.Parameters.AddWithValue("case",caseRef);
        return (long)(await query.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task ThreeClaimedStagesPersistWithExactlyPairedImmutableAudit()
    {
        var (db,store)=await OpenAsync();await using var _=db;
        var id=Case();var initial=Root();
        Assert.Equal(SyntheticAccessNoteOutcome.RecordedUnverified,
            (await store.ApplyAsync(id,initial,TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SyntheticAccessNoteOutcome.RecordedUnverified,
            (await store.ApplyAsync(id,Concern(initial),TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SyntheticAccessNoteOutcome.RecordedUnverified,
            (await store.ApplyAsync(id,Oversight(initial,2),TestContext.Current.CancellationToken)).Outcome);
        var source=await store.ReadAsync(id,TestContext.Current.CancellationToken);
        Assert.Equal(3,source.Revision);
        Assert.Equal(3,await CountAsync("internal_test_clinical_access_event",id));
        Assert.Equal(3,await CountAsync("internal_test_clinical_access_audit",id));
        var member=await store.ProjectForClaimedMemberAsync(id,Viewer,TestContext.Current.CancellationToken);
        Assert.Equal(SyntheticAccessTransparencyState.UnverifiedPaperOnly,member.State);
        Assert.Equal(1,member.UnverifiedMemberConcernCount);
        Assert.Equal(1,member.UnverifiedOversightNoteCount);
        Assert.False(member.DisclosureGranted);
        Assert.False(member.RecordReadOccurred);
        Assert.False(member.LegalExceptionEstablished);
    }

    [Fact]
    public async Task WrongMemberOrSponsorReceivesZeroLineageAndCounts()
    {
        var (db,store)=await OpenAsync();await using var _=db;
        var id=Case();var first=Root();
        await store.ApplyAsync(id,first,TestContext.Current.CancellationToken);
        var sponsor=Viewer with {ActorKind=SelfReadActorKind.Sponsor};
        var wrongMember=Viewer with {ServerResolvedMemberRef="test:other-member"};
        foreach(var viewer in new[]{sponsor,wrongMember})
        {
            var projected=await store.ProjectForClaimedMemberAsync(id,viewer,TestContext.Current.CancellationToken);
            Assert.NotEqual(SyntheticAccessTransparencyState.UnverifiedPaperOnly,
                projected.State);
            Assert.Equal(0,projected.Revision);
            Assert.Equal(0,projected.UnverifiedRequestCount);
            Assert.Null(projected.UntrustedPrerequisiteAssessment);
            Assert.False(projected.RecordReadOccurred);
            Assert.False(projected.DisclosureGranted);
        }
        var unknown=await store.ProjectForClaimedMemberAsync(Case(),Viewer,TestContext.Current.CancellationToken);
        Assert.Equal(SyntheticAccessTransparencyState.Unavailable,unknown.State);
    }

    [Fact]
    public async Task ExactReplayAndChangedPayloadAreDifferentAfterThreeNotes()
    {
        var (db,store)=await OpenAsync();await using var _=db;
        var id=Case();var root=Root();var concern=Concern(root);
        await store.ApplyAsync(id,root,TestContext.Current.CancellationToken);
        await store.ApplyAsync(id,concern);
        await store.ApplyAsync(id,Oversight(root,2),TestContext.Current.CancellationToken);
        Assert.Equal(SyntheticAccessNoteOutcome.Replayed,
            (await store.ApplyAsync(id,root,TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SyntheticAccessNoteOutcome.Replayed,
            (await store.ApplyAsync(id,concern,TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SyntheticAccessNoteOutcome.ConflictingReplay,
            (await store.ApplyAsync(id,concern with{
                UnverifiedEvidenceRef="test:changed"},TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(3,await CountAsync("internal_test_clinical_access_audit",id));
    }

    [Fact]
    public async Task ForgedScopeDuplicateStageAndWrongOriginalCannotAppend()
    {
        var (db,store)=await OpenAsync();await using var _=db;
        var id=Case();var first=Root();
        await store.ApplyAsync(id,first,TestContext.Current.CancellationToken);
        var forged=Concern(first) with{
            ClaimedDisclosureScope=first.ClaimedDisclosureScope with{
                MemberRef="test:other"}};
        Assert.Equal(SyntheticAccessNoteOutcome.ScopeChanged,
            (await store.ApplyAsync(id,forged,TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SyntheticAccessNoteOutcome.ScopeChanged,
            (await store.ApplyAsync(id,Concern(first) with{
                OriginalRequestId=Guid.NewGuid()},TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SyntheticAccessNoteOutcome.RecordedUnverified,
            (await store.ApplyAsync(id,Concern(first),TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SyntheticAccessNoteOutcome.DuplicateStage,
            (await store.ApplyAsync(id,Concern(first,2),TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(2,(await store.ReadAsync(id,TestContext.Current.CancellationToken)).Revision);
    }

    [Fact]
    public async Task ConcurrentDifferentClaimedNotesSerializeToOneAcceptedAndOneStale()
    {
        var (db,store)=await OpenAsync();await using var _=db;
        var id=Case();var first=Root();await store.ApplyAsync(id,first,TestContext.Current.CancellationToken);
        var a=Concern(first);var b=Oversight(first);
        var results=await Task.WhenAll(store.ApplyAsync(id,a,TestContext.Current.CancellationToken),store.ApplyAsync(id,b));
        Assert.Single(results,x=>x.Outcome==SyntheticAccessNoteOutcome.RecordedUnverified);
        Assert.Single(results,x=>x.Outcome==SyntheticAccessNoteOutcome.StaleRevision);
        Assert.Equal(2,await CountAsync("internal_test_clinical_access_audit",id));
    }

    [Fact]
    public async Task CorruptedHeaderFailsBeforeReadAndEvenExactReplay()
    {
        var (db,store)=await OpenAsync();await using var _=db;
        var id=Case();var first=Root();await store.ApplyAsync(id,first,TestContext.Current.CancellationToken);
        await using var owner=NpgsqlDataSource.Create(Connection);
        await using(var cn=await owner.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using(var sql=new NpgsqlCommand("""
            UPDATE internal_test_clinical_access_case SET current_revision=2
            WHERE case_ref=@case
            """,cn))
        {
            sql.Parameters.AddWithValue("case",id);
            Assert.Equal(1,await sql.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }
        var read=await Assert.ThrowsAsync<InvalidOperationException>(
            ()=>store.ReadAsync(id,TestContext.Current.CancellationToken));
        Assert.Equal("TEST_CLINICAL_ACCESS_HISTORY_UNVERIFIABLE",read.Message);
        var replay=await Assert.ThrowsAsync<InvalidOperationException>(
            ()=>store.ApplyAsync(id,first,TestContext.Current.CancellationToken));
        Assert.Equal("TEST_CLINICAL_ACCESS_HISTORY_UNVERIFIABLE",replay.Message);
    }

    [Fact]
    public async Task RuntimeCannotRewriteAuditOrEventAndCaseIsIndependent()
    {
        var (db,store)=await OpenAsync();await using var _=db;
        var id=Case();await store.ApplyAsync(id,Root(),TestContext.Current.CancellationToken);
        await using var cn=await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        foreach(var sqlText in new[]{
            "UPDATE internal_test_clinical_access_event SET action_code=2 WHERE case_ref=@case",
            "DELETE FROM internal_test_clinical_access_audit WHERE case_ref=@case"})
        {
            await using var cmd=new NpgsqlCommand(sqlText,cn);
            cmd.Parameters.AddWithValue("case",id);
            var failure=await Assert.ThrowsAsync<PostgresException>(
                async ()=>await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501",failure.SqlState);
        }
        Assert.Equal(0,(await store.ReadAsync(Case(),TestContext.Current.CancellationToken)).Revision);
        Assert.Equal(1,(await store.ReadAsync(id,TestContext.Current.CancellationToken)).Revision);
    }
}
