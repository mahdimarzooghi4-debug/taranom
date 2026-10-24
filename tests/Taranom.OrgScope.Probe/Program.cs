using Npgsql;
using Taranom.OrgScope.Foundation;

// Real PostgreSQL 18, two distinct non-superuser logins, no external IdP.
if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
    "CI_EPHEMERAL_ONLY") throw new InvalidOperationException("SCOPE_PROBE_TEST_ONLY");
var raw = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
    ?? throw new InvalidOperationException("SCOPE_PROBE_DATABASE_MISSING");
var cfg = new NpgsqlConnectionStringBuilder(raw);
if (cfg.Host is not ("localhost" or "127.0.0.1") || cfg.Port != 5432 ||
    cfg.Database != "taranom_sprint64_center" || cfg.Username != "postgres")
    throw new InvalidOperationException("SCOPE_PROBE_WRONG_DATABASE");
var aSecret = Environment.GetEnvironmentVariable("TARANOM_ORG_SUPPORT_TEST_PASSWORD");
var bSecret = Environment.GetEnvironmentVariable("TARANOM_ORG_CORPORATE_TEST_PASSWORD");
await using var owner = NpgsqlDataSource.Create(cfg.ConnectionString);
await PgSyntheticOrganizationScope.InitializeAsync(owner,aSecret,bSecret);
await using var a = PgSyntheticOrganizationScope.ScopedSource(
    cfg,PgSyntheticOrganizationScope.SupportRole,aSecret!);
await using var b = PgSyntheticOrganizationScope.ScopedSource(
    cfg,PgSyntheticOrganizationScope.CorporateRole,bSecret!);

async Task<long> Count(NpgsqlDataSource ds,string sql)
{
    await using var connection=await ds.OpenConnectionAsync();
    await using var cmd=new NpgsqlCommand(sql,connection);
    return (long)(await cmd.ExecuteScalarAsync() ?? -1L);
}
async Task Execute(NpgsqlDataSource ds,string sql)
{
    await using var connection=await ds.OpenConnectionAsync();
    await using var cmd=new NpgsqlCommand(sql,connection);
    await cmd.ExecuteNonQueryAsync();
}
async Task MustDeny(NpgsqlDataSource ds,string sql,string test)
{
    try{await Execute(ds,sql);}
    catch (PostgresException e) when(e.SqlState=="42501"){return;}
    throw new InvalidOperationException("ORG_SCOPE_PRIVILEGE_ESCALATION:"+test);
}
async Task AssertPrincipal(NpgsqlDataSource ds,string expected)
{
    await using var connection=await ds.OpenConnectionAsync();
    await using var cmd=new NpgsqlCommand("""
        SELECT current_user,session_user,rolsuper,rolbypassrls,rolcreaterole,
               rolinherit,
               has_table_privilege(current_user,
                  'public.internal_test_org_scope_binding','SELECT'),
               has_table_privilege(current_user,
                  'public.internal_test_org_draft_item','SELECT'),
               has_table_privilege(current_user,
                  'public.internal_test_org_draft_item','INSERT'),
               has_table_privilege(current_user,
                  'public.internal_test_org_draft_item','UPDATE'),
               has_table_privilege(current_user,
                  'public.internal_test_org_draft_item','DELETE')
        FROM pg_roles WHERE rolname=current_user
        """,connection);
    await using var reader=await cmd.ExecuteReaderAsync();
    if(!await reader.ReadAsync()||
       reader.GetString(0)!=expected||reader.GetString(1)!=expected||
       reader.GetBoolean(2)||reader.GetBoolean(3)||reader.GetBoolean(4)||
       reader.GetBoolean(5)||reader.GetBoolean(6)||
       !reader.GetBoolean(7)||!reader.GetBoolean(8)||
       reader.GetBoolean(9)||reader.GetBoolean(10))
        throw new InvalidOperationException("ORG_SCOPE_ROLE_RIGHTS_INVALID");
}
await AssertPrincipal(a,PgSyntheticOrganizationScope.SupportRole);
await AssertPrincipal(b,PgSyntheticOrganizationScope.CorporateRole);
var id="test:draft-"+Guid.NewGuid().ToString("N");
await Execute(a,$"""
    INSERT INTO public.internal_test_org_draft_item
      (scope_ref,item_ref,source_ref)
    VALUES ('test:scope-support','{id}','test:unverified-support-source')
    """);
await Execute(b,$"""
    INSERT INTO public.internal_test_org_draft_item
      (scope_ref,item_ref,source_ref)
    VALUES ('test:scope-corporate','{id}','test:unverified-corporate-source')
    """);
if(await Count(a,"SELECT count(*) FROM public.internal_test_org_draft_item")!=1||
   await Count(b,"SELECT count(*) FROM public.internal_test_org_draft_item")!=1)
    throw new InvalidOperationException("ORG_SCOPE_CROSS_READ");
if(await Count(a,"SELECT count(*) FROM public.internal_test_org_draft_item WHERE scope_ref='test:scope-corporate'")!=0||
   await Count(b,"SELECT count(*) FROM public.internal_test_org_draft_item WHERE scope_ref='test:scope-support'")!=0)
    throw new InvalidOperationException("ORG_SCOPE_CROSS_FILTER");
await MustDeny(a,"""
    INSERT INTO public.internal_test_org_draft_item
     (scope_ref,item_ref,source_ref)
    VALUES ('test:scope-corporate','test:injection','test:fake')
    ""","CROSS_INSERT");
await MustDeny(b,"""
    INSERT INTO public.internal_test_org_draft_item
     (scope_ref,item_ref,source_ref)
    VALUES ('test:scope-support','test:injection','test:fake')
    ""","REVERSE_CROSS_INSERT");
await MustDeny(a,"SELECT * FROM public.internal_test_org_scope_binding","BINDING_READ");
await MustDeny(a,"SET ROLE taranom_scope_corporate_test","ROLE_SWITCH");
await MustDeny(b,"SET ROLE taranom_scope_support_test","REVERSE_ROLE_SWITCH");
await MustDeny(a,"""
    UPDATE public.internal_test_org_draft_item SET source_ref='test:changed'
    ""","UPDATE_DENIED");
await MustDeny(a,"DELETE FROM public.internal_test_org_draft_item","DELETE_DENIED");
await MustDeny(b,"TRUNCATE public.internal_test_org_draft_item","TRUNCATE_DENIED");
await MustDeny(b,"CREATE TABLE public.org_scope_escalation(id int)","DDL_DENIED");
await MustDeny(a,"GRANT SELECT ON public.internal_test_org_scope_binding TO public","GRANT_DENIED");
if(await Count(a,"SELECT count(*) FROM public.internal_test_org_draft_item")!=1||
   await Count(b,"SELECT count(*) FROM public.internal_test_org_draft_item")!=1)
    throw new InvalidOperationException("ORG_SCOPE_MUTATED_AFTER_DENIAL");
Console.WriteLine("PASS: real per-organization PostgreSQL session_user RLS and 10 denied cross-scope/escalation attempts; no real identity granted");

//
// TRN-TECH-132: real bounded application flow (no public HTTP/session):
// Record -> deterministic exact replay -> scoped Read -> cursor List.
//
var support = new PgScopedDraftWorkspace(a);
var company = new PgScopedDraftWorkspace(b);
var prefix = "test:zz-workspace-" + Guid.NewGuid().ToString("N");
var first = prefix + "-a";
var second = prefix + "-b";
var third = prefix + "-c";
var original = "test:unverified-draft-source-a";
var different = "test:unverified-draft-source-conflict";
var recorded = await support.RecordAsync(first, original);
if (recorded.Status != DraftWriteStatus.Recorded ||
    recorded.Draft?.ScopeRef != PgSyntheticOrganizationScope.SupportRef ||
    recorded.Draft.SourceRef != original)
    throw new InvalidOperationException("ORG_WORKSPACE_RECORD_FAILED");
var replay = await support.RecordAsync(first, original);
if (replay.Status != DraftWriteStatus.Replayed ||
    replay.Draft != recorded.Draft)
    throw new InvalidOperationException("ORG_WORKSPACE_REPLAY_FAILED");
var conflicting = await support.RecordAsync(first, different);
if (conflicting.Status != DraftWriteStatus.ConflictingReplay ||
    conflicting.Draft != recorded.Draft)
    throw new InvalidOperationException("ORG_WORKSPACE_REPLAY_CHANGED_DATA");

// Equal item refs MUST be independent across organizations.
var otherOrg = await company.RecordAsync(first, different);
if (otherOrg.Status != DraftWriteStatus.Recorded ||
    otherOrg.Draft?.ScopeRef != PgSyntheticOrganizationScope.CorporateRef)
    throw new InvalidOperationException("ORG_WORKSPACE_CROSS_ORG_COLLISION");
if (await company.ReadAsync(first) != otherOrg.Draft ||
    await support.ReadAsync(first) != recorded.Draft)
    throw new InvalidOperationException("ORG_WORKSPACE_CROSS_ORG_READ");

// Technical metadata only; bogus real identifiers are rejected before SQL.
if ((await support.RecordAsync("national-id:real",original)).Status !=
        DraftWriteStatus.InvalidRequest ||
    (await company.RecordAsync(second,"https://provider.example")).Status !=
        DraftWriteStatus.InvalidRequest)
    throw new InvalidOperationException("ORG_WORKSPACE_ACCEPTED_LIVE_DATA");
try
{
    await support.ListAsync("untrusted:cursor",2);
    throw new InvalidOperationException("ORG_WORKSPACE_CURSOR_ACCEPTED");
}
catch (ArgumentException) { }

// Multiple independent connections competing for one (scope,item) produce
// exactly one INSERT and all subsequent attempts replay unchanged.
var concurrent = await Task.WhenAll(Enumerable.Range(0,8)
    .Select(_ => support.RecordAsync(second,"test:concurrent-source")));
if (concurrent.Count(v => v.Status == DraftWriteStatus.Recorded) != 1 ||
    concurrent.Count(v => v.Status == DraftWriteStatus.Replayed) != 7)
    throw new InvalidOperationException("ORG_WORKSPACE_DUPLICATE_RACE");

var last = await support.RecordAsync(third,"test:unverified-draft-source-c");
if (last.Status != DraftWriteStatus.Recorded)
    throw new InvalidOperationException("ORG_WORKSPACE_THIRD_RECORD");
var pageOne = await support.ListAsync(prefix,2);
if (pageOne.Items.Count != 2 ||
    pageOne.Items[0].ItemRef != first ||
    pageOne.Items[1].ItemRef != second ||
    pageOne.NextAfterItemRef != second)
    throw new InvalidOperationException("ORG_WORKSPACE_PAGE_ONE");
var pageTwo = await support.ListAsync(pageOne.NextAfterItemRef,2);
if (pageTwo.Items.Count != 1 ||
    pageTwo.Items[0].ItemRef != third ||
    pageTwo.NextAfterItemRef is not null)
    throw new InvalidOperationException("ORG_WORKSPACE_PAGE_TWO");
var corporateList = await company.ListAsync(prefix,2);
if (corporateList.Items.Count != 1 ||
    corporateList.Items[0] != otherOrg.Draft ||
    corporateList.NextAfterItemRef is not null)
    throw new InvalidOperationException("ORG_WORKSPACE_SCOPE_FILTER_FAIL");
if (await company.ReadAsync(second) is not null ||
    await company.ReadAsync(third) is not null)
    throw new InvalidOperationException("ORG_WORKSPACE_EXPOSED_OTHER_SCOPE");

// A database schema owner is NOT an organizational operator. Passing an
// owner datasource to the application service must fail closed.
var ownerWorkspace = new PgScopedDraftWorkspace(owner);
try
{
    await ownerWorkspace.ReadAsync(first);
    throw new InvalidOperationException("ORG_WORKSPACE_OWNER_BYPASS");
}
catch (InvalidOperationException e) when (
    e.Message == "ORG_DRAFT_UNTRUSTED_PRINCIPAL" ||
    e.Message == "ORG_DRAFT_SCOPE_UNVERIFIABLE") { }
Console.WriteLine("PASS: PostgreSQL organization draft workspace, two scoped reads, atomic idempotent replay, 8-way concurrency and cursor paging");

//
// TRN-TECH-133 — real audit integrity, no mocked repository or role.
// Creating each scoped draft emits exactly one immutable CREATE event in
// the same PostgreSQL transaction; repeats/conflicts must not append events.
//
var aAudit = await support.ReadAuditAsync(first);
var bAudit = await company.ReadAuditAsync(first);
if (aAudit is null || bAudit is null ||
    aAudit.ScopeRef != PgSyntheticOrganizationScope.SupportRef ||
    bAudit.ScopeRef != PgSyntheticOrganizationScope.CorporateRef ||
    aAudit.SourceRef != original || bAudit.SourceRef != different ||
    aAudit.EventNo != 1 || bAudit.EventNo != 1 ||
    aAudit.EventType != "DRAFT_RECORDED_TEST_ONLY" ||
    bAudit.EventType != "DRAFT_RECORDED_TEST_ONLY" ||
    aAudit.EventSha256.Length != 64 ||
    bAudit.EventSha256.Length != 64 ||
    aAudit.EventSha256 == bAudit.EventSha256)
    throw new InvalidOperationException("ORG_AUDIT_SOURCE_BINDING_FAILED");
if (await company.ReadAuditAsync(second) is not null ||
    await company.ReadAuditAsync(third) is not null)
    throw new InvalidOperationException("ORG_AUDIT_CROSS_SCOPE_READ");

if (await Count(a,"SELECT count(*) FROM public.internal_test_org_draft_audit") != 4 ||
    await Count(b,"SELECT count(*) FROM public.internal_test_org_draft_audit") != 2)
    throw new InvalidOperationException("ORG_AUDIT_DUPLICATE_ON_REPLAY");
if (await Count(a,"""
    SELECT count(*) FROM public.internal_test_org_draft_audit
    WHERE scope_ref='test:scope-corporate'
    """) != 0)
    throw new InvalidOperationException("ORG_AUDIT_ROW_SECURITY_BYPASS");

// Neither org login can forge its own audit history, let alone another's.
await MustDeny(a,"""
    INSERT INTO public.internal_test_org_draft_audit
    (scope_ref,item_ref,event_no,event_type,source_ref,event_sha256)
    VALUES ('test:scope-support','test:fake',1,
            'DRAFT_RECORDED_TEST_ONLY','test:fake',repeat('A',64))
    ""","AUDIT_FORGE");
await MustDeny(b,"""
    UPDATE public.internal_test_org_draft_audit SET event_sha256=repeat('B',64)
    ""","AUDIT_UPDATE");
await MustDeny(b,"DELETE FROM public.internal_test_org_draft_audit",
    "AUDIT_DELETE");
await MustDeny(a,"TRUNCATE public.internal_test_org_draft_audit",
    "AUDIT_TRUNCATE");

// Database owner may manage CI fixture schema, but SQL row mutation is
// specifically refused by immutable audit triggers, even for that owner.
await MustDeny(owner,$"""
    UPDATE public.internal_test_org_draft_audit
       SET event_sha256=repeat('C',64)
     WHERE scope_ref='test:scope-support' AND item_ref='{first}'
    ""","OWNER_AUDIT_UPDATE");
await MustDeny(owner,$"""
    DELETE FROM public.internal_test_org_draft_audit
     WHERE scope_ref='test:scope-support' AND item_ref='{first}'
    ""","OWNER_AUDIT_DELETE");
await MustDeny(owner,
    "TRUNCATE public.internal_test_org_draft_audit",
    "OWNER_AUDIT_TRUNCATE");

// Simulated tampering of the *draft* itself by the fixture schema owner
// must be detected by application reads, list pages and exact replay.
await Execute(owner,$"""
    UPDATE public.internal_test_org_draft_item
       SET source_ref='test:tampered-by-schema-owner'
     WHERE scope_ref='test:scope-support' AND item_ref='{first}'
    """);
try
{
    try
    {
        await support.ReadAsync(first);
        throw new InvalidOperationException("ORG_AUDIT_TAMPER_UNDETECTED");
    }
    catch (InvalidOperationException e)
        when (e.Message=="ORG_DRAFT_AUDIT_INTEGRITY_MISMATCH") { }
    try
    {
        await support.ListAsync(prefix,2);
        throw new InvalidOperationException("ORG_AUDIT_PAGE_TAMPER_UNDETECTED");
    }
    catch (InvalidOperationException e)
        when (e.Message=="ORG_DRAFT_AUDIT_INTEGRITY_MISMATCH") { }
    try
    {
        await support.RecordAsync(first,original);
        throw new InvalidOperationException("ORG_AUDIT_REPLAY_TAMPER_UNDETECTED");
    }
    catch (InvalidOperationException e)
        when (e.Message=="ORG_DRAFT_AUDIT_INTEGRITY_MISMATCH") { }
}
finally
{
    await Execute(owner,$"""
        UPDATE public.internal_test_org_draft_item
           SET source_ref='{original}'
         WHERE scope_ref='test:scope-support' AND item_ref='{first}'
        """);
}
if (await support.ReadAsync(first) != recorded.Draft ||
    await support.ReadAuditAsync(first) != aAudit ||
    await Count(a,"SELECT count(*) FROM public.internal_test_org_draft_audit")!=4)
    throw new InvalidOperationException("ORG_AUDIT_RECOVERY_MISMATCH");
Console.WriteLine("PASS: atomic per-scope immutable draft audit, independent digest verification, replay and owner-tamper denial");


// TRN-TECH-134: real PostgreSQL per-organization technical activity,
// expected-revision CAS, retry isolation and immutable chain verification.
var supportActivity = new PgScopedDraftActivity(a);
var corporateActivity = new PgScopedDraftActivity(b);
var activityRequest = "test:activity-request-" + Guid.NewGuid().ToString("N");
var supportNote = "test:support-note-a";
var corporateNote = "test:corporate-note-a";
var aFirst = await supportActivity.AppendAsync(
    first,activityRequest,supportNote,0);
var bFirst = await corporateActivity.AppendAsync(
    first,activityRequest,corporateNote,0);
if (aFirst.Status != DraftActivityStatus.Recorded ||
    bFirst.Status != DraftActivityStatus.Recorded ||
    aFirst.CurrentNo != 1 || bFirst.CurrentNo != 1)
    throw new InvalidOperationException("ORG_ACTIVITY_SCOPE_CREATE_FAILED");

// Exact idempotent retry survives subsequent operations; a changed retry
// never mutates the original activity.
var same = await supportActivity.AppendAsync(first,activityRequest,supportNote,0);
var conflict = await supportActivity.AppendAsync(
    first,activityRequest,"test:different-note",0);
if (same.Status != DraftActivityStatus.Replayed || same.CurrentNo != 1 ||
    conflict.Status != DraftActivityStatus.ConflictingReplay ||
    conflict.CurrentNo != 1)
    throw new InvalidOperationException("ORG_ACTIVITY_RETRY_INTEGRITY");

var secondRequest = "test:activity-request-two-" + Guid.NewGuid().ToString("N");
var stale = await supportActivity.AppendAsync(
    first,secondRequest,"test:note-b",0);
var next = await supportActivity.AppendAsync(
    first,secondRequest,"test:note-b",1);
var lateExact = await supportActivity.AppendAsync(
    first,activityRequest,supportNote,0);
if (stale.Status != DraftActivityStatus.StaleRevision || stale.CurrentNo != 1 ||
    next.Status != DraftActivityStatus.Recorded || next.CurrentNo != 2 ||
    lateExact.Status != DraftActivityStatus.Replayed || lateExact.CurrentNo != 1)
    throw new InvalidOperationException("ORG_ACTIVITY_CAS_FAILED");

// Eight independent concurrent connections compete to append the SAME
// request at revision 2. Exactly one writes and seven replay.
var thirdRequest = "test:activity-request-three-" + Guid.NewGuid().ToString("N");
var concurrentActivity = await Task.WhenAll(Enumerable.Range(0,8)
    .Select(_=>supportActivity.AppendAsync(
        first,thirdRequest,"test:note-c",2)));
if (concurrentActivity.Count(x=>x.Status==DraftActivityStatus.Recorded)!=1 ||
    concurrentActivity.Count(x=>x.Status==DraftActivityStatus.Replayed)!=7 ||
    concurrentActivity.Any(x=>x.CurrentNo!=3))
    throw new InvalidOperationException("ORG_ACTIVITY_CONCURRENT_DUPLICATE");

var aHistory = await supportActivity.ReadHistoryAsync(first);
var bHistory = await corporateActivity.ReadHistoryAsync(first);
if (aHistory.Count!=3 || bHistory.Count!=1 ||
    aHistory[0].RequestRef!=activityRequest ||
    aHistory[1].RequestRef!=secondRequest ||
    aHistory[2].RequestRef!=thirdRequest ||
    aHistory[0].PreviousSha256!="GENESIS" ||
    aHistory[1].PreviousSha256!=aHistory[0].ActivitySha256 ||
    aHistory[2].PreviousSha256!=aHistory[1].ActivitySha256 ||
    bHistory[0].NoteRef!=corporateNote ||
    bHistory[0].ActivitySha256==aHistory[0].ActivitySha256 ||
    (await corporateActivity.ReadHistoryAsync(second)).Count!=0 ||
    (await supportActivity.ReadHistoryAsync(third)).Count!=0)
    throw new InvalidOperationException("ORG_ACTIVITY_HISTORY_CROSS_SCOPE");

if ((await supportActivity.AppendAsync(first,"live:person","test:note",3))
        .Status!=DraftActivityStatus.InvalidRequest ||
    (await supportActivity.AppendAsync(first,"test:new","note with raw text",3))
        .Status!=DraftActivityStatus.InvalidRequest ||
    (await supportActivity.AppendAsync("test:no-such-item",
        "test:request","test:note",0)).Status!=DraftActivityStatus.NotFound)
    throw new InvalidOperationException("ORG_ACTIVITY_ACCEPTED_UNVERIFIED_INPUT");

if(await Count(a,"SELECT count(*) FROM public.internal_test_org_draft_activity")!=3 ||
   await Count(b,"SELECT count(*) FROM public.internal_test_org_draft_activity")!=1 ||
   await Count(a,"SELECT count(*) FROM public.internal_test_org_draft_activity WHERE scope_ref='test:scope-corporate'")!=0)
    throw new InvalidOperationException("ORG_ACTIVITY_RLS_EXPOSURE");

// Test-logins cannot forge even their own activity with direct SQL.
await MustDeny(a,"""
    INSERT INTO public.internal_test_org_draft_activity
      (scope_ref,item_ref,activity_no,request_ref,note_ref,
       expected_previous_no,previous_sha256,activity_sha256)
    VALUES ('test:scope-support','test:forged',1,'test:r','test:n',
            0,'GENESIS',repeat('A',64))
    ""","ACTIVITY_DIRECT_INSERT");
await MustDeny(a,"""
    UPDATE public.internal_test_org_draft_activity SET note_ref='test:tamper'
    ""","ACTIVITY_UPDATE");
await MustDeny(b,"DELETE FROM public.internal_test_org_draft_activity",
    "ACTIVITY_DELETE");
await MustDeny(b,"TRUNCATE public.internal_test_org_draft_activity",
    "ACTIVITY_TRUNCATE");
await MustDeny(owner,$"""
    UPDATE public.internal_test_org_draft_activity
       SET note_ref='test:owner-tamper'
     WHERE scope_ref='test:scope-support' AND item_ref='{first}'
    ""","ACTIVITY_OWNER_UPDATE");
await MustDeny(owner,$"""
    DELETE FROM public.internal_test_org_draft_activity
     WHERE scope_ref='test:scope-support' AND item_ref='{first}'
    ""","ACTIVITY_OWNER_DELETE");
await MustDeny(owner,
    "TRUNCATE public.internal_test_org_draft_activity",
    "ACTIVITY_OWNER_TRUNCATE");
await MustDeny(owner,$"""
    SELECT * FROM public.internal_test_org_activity_append(
      '{first}','test:owner-request','test:owner-note',3)
    ""","ACTIVITY_OWNER_FUNCTION_DENIED");

// Admin modification of a draft source must also stop an activity journal
// read and append, without destroying immutable history.
await Execute(owner,$"""
    UPDATE public.internal_test_org_draft_item
       SET source_ref='test:tampered-activity-parent'
     WHERE scope_ref='test:scope-support' AND item_ref='{first}'
    """);
try
{
    try
    {
        await supportActivity.ReadHistoryAsync(first);
        throw new InvalidOperationException("ORG_ACTIVITY_PARENT_TAMPER_UNDETECTED");
    }
    catch (InvalidOperationException e)
        when (e.Message=="ORG_DRAFT_AUDIT_INTEGRITY_MISMATCH") { }
    try
    {
        await supportActivity.AppendAsync(first,
            "test:fourth-request","test:fourth-note",3);
        throw new InvalidOperationException("ORG_ACTIVITY_APPEND_PARENT_TAMPER");
    }
    catch (InvalidOperationException e)
        when (e.Message=="ORG_DRAFT_AUDIT_INTEGRITY_MISMATCH") { }
}
finally
{
    await Execute(owner,$"""
        UPDATE public.internal_test_org_draft_item
           SET source_ref='{original}'
         WHERE scope_ref='test:scope-support' AND item_ref='{first}'
        """);
}
var afterRestore = await supportActivity.ReadHistoryAsync(first);
if (!aHistory.SequenceEqual(afterRestore) ||
    await Count(a,"SELECT count(*) FROM public.internal_test_org_draft_activity")!=3)
    throw new InvalidOperationException("ORG_ACTIVITY_TAMPER_RECOVERY_FAILED");
Console.WriteLine("PASS: append-only organization activity, revision CAS, 8-way idempotency, RLS, immutable hash chain and tamper recovery");


// TRN-TECH-136: one MVCC snapshot verifies CREATE audit and the entire
// hash-chained activity for all returned rows AND the lookahead.
var sq = new PgScopedDraftWorkQueue(a);
var cq = new PgScopedDraftWorkQueue(b);
var q1=await sq.ReadAsync(prefix,2);
var q2=await sq.ReadAsync(q1.NextAfterItemRef,2);
var qc=await cq.ReadAsync(prefix,2);
if(q1.Items.Count!=2||q1.Items[0].ItemRef!=first||
   q1.Items[0].ActivityCount!=3||
   q1.Items[0].LastNoteRef!="test:note-c"||
   q1.Items[1].ItemRef!=second||q1.Items[1].ActivityCount!=0||
   q1.Items[1].LastRequestRef is not null||
   q1.NextAfterItemRef!=second||
   q2.Items.Count!=1||q2.Items[0].ItemRef!=third||
   q2.NextAfterItemRef is not null||
   qc.Items.Count!=1||qc.Items[0].ItemRef!=first||
   qc.Items[0].ActivityCount!=1||
   qc.Items[0].LastNoteRef!=corporateNote||
   qc.NextAfterItemRef is not null)
    throw new InvalidOperationException("ORG_QUEUE_COHERENCE_FAILED");

// An invalid reference cannot become a scope override/cursor.
try
{
    await sq.ReadAsync("person:private",2);
    throw new InvalidOperationException("ORG_QUEUE_BAD_CURSOR_ACCEPTED");
}
catch(ArgumentException){ }

// Owner-tampered original source invalidates entire workqueue, even when
// the corrupt row is only the lookahead that would not be returned.
await Execute(owner,$"""
    UPDATE public.internal_test_org_draft_item
       SET source_ref='test:corrupted-lookahead'
     WHERE scope_ref='test:scope-support' AND item_ref='{third}'
    """);
try
{
    try
    {
        await sq.ReadAsync(prefix,2);
        throw new InvalidOperationException("ORG_QUEUE_LOOKAHEAD_TAMPER_UNDETECTED");
    }
    catch(InvalidOperationException e) when(
        e.Message=="ORG_DRAFT_AUDIT_INTEGRITY_MISMATCH"){}
}
finally
{
    await Execute(owner,$"""
        UPDATE public.internal_test_org_draft_item
           SET source_ref='test:unverified-draft-source-c'
         WHERE scope_ref='test:scope-support' AND item_ref='{third}'
        """);
}
if((await sq.ReadAsync(prefix,2)).Items[0].ActivityCount!=3)
    throw new InvalidOperationException("ORG_QUEUE_RESTORE_FAILED");
Console.WriteLine("PASS: one-snapshot RLS-scoped workqueue, exact last-activity projection, cursor and lookahead audit fail-closed");


// TRN-TECH-137: historical prefix and page cursors are pinned to real
// PostgreSQL current revision and use fully reattested activity lineage.
var pageA=await supportActivity.ReadPageAsync(first,0,2,3,3);
var pageB=await supportActivity.ReadPageAsync(first,2,2,3,3);
var oldPrefix=await supportActivity.ReadPageAsync(first,0,2,3,2);
var genesis=await supportActivity.ReadPageAsync(first,0,2,3,0);
var corp=await corporateActivity.ReadPageAsync(first,0,2,1,1);
var absent=await corporateActivity.ReadPageAsync(second,0,2,0,0);
var stalePin=await supportActivity.ReadPageAsync(first,0,2,2,2);
if(pageA is null||pageB is null||oldPrefix is null||genesis is null||
   corp is null||pageA.CurrentNo!=3||pageA.Events.Count!=2||
   pageA.Events[0].NoteRef!=supportNote||
   pageA.Events[1].NoteRef!="test:note-b"||
   !pageA.HasMore||pageA.NextAfterNo!=2||
   pageB.Events.Count!=1||pageB.Events[0].NoteRef!="test:note-c"||
   pageB.HasMore||pageB.NextAfterNo is not null||
   oldPrefix.Events.Count!=2||oldPrefix.HasMore||
   oldPrefix.NextAfterNo is not null||oldPrefix.AsOfNo!=2||
   genesis.Events.Count!=0||genesis.HasMore||
   corp.Events.Count!=1||corp.Events[0].NoteRef!=corporateNote||
   absent is not null||stalePin is null||!stalePin.StaleRevision||
   stalePin.CurrentNo!=3||stalePin.Events.Count!=0)
    throw new InvalidOperationException("ORG_TIMELINE_REVISION_PAGE_FAILED");
try
{
    await supportActivity.ReadPageAsync(first,-1,2,3,3);
    throw new InvalidOperationException("ORG_TIMELINE_BAD_CURSOR_ACCEPTED");
}
catch(ArgumentException){}
try
{
    await supportActivity.ReadPageAsync(first,0,2,3,4);
    throw new InvalidOperationException("ORG_TIMELINE_FUTURE_VERSION_ACCEPTED");
}
catch(ArgumentException){}

// Append AFTER a historical read: old pinned pages must be refused.
var fourthReq="test:timeline-fourth-"+Guid.NewGuid().ToString("N");
var fourth=await supportActivity.AppendAsync(first,fourthReq,
    "test:timeline-fourth-note",3);
if(fourth.Status!=DraftActivityStatus.Recorded ||
   !(await supportActivity.ReadPageAsync(first,0,2,3,2))!.StaleRevision)
    throw new InvalidOperationException("ORG_TIMELINE_STALE_AFTER_APPEND");
var fresh=await supportActivity.ReadPageAsync(first,2,2,4,4);
if(fresh is null||fresh.Events.Count!=2||
   fresh.Events[1].NoteRef!="test:timeline-fourth-note")
    throw new InvalidOperationException("ORG_TIMELINE_NEW_VERSION_MISSING");
Console.WriteLine("PASS: pinned PostgreSQL activity pagination, as-of history, independent tenant, and stale revision refusal");
