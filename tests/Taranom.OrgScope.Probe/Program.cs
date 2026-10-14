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
Console.WriteLine("PASS: real per-organization PostgreSQL session_user RLS and 9 denied cross-scope/escalation attempts; no real identity granted");
