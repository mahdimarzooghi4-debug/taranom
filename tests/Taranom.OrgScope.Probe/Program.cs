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


