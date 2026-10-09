using Npgsql;
using Taranom.SyntheticFoundation;

static void Check(bool expression, string message)
{
    if (!expression) throw new Exception("TEST_FAILURE: " + message);
    Console.WriteLine("PASS " + message);
}

static async Task ExpectDenial(Func<Task> action, string code, string message)
{
    try { await action(); }
    catch (InvalidOperationException ex) when (ex.Message == code)
    {
        Console.WriteLine("PASS " + message);
        return;
    }
    throw new Exception("TEST_FAILURE: expected specific denial " + code + " for " + message);
}

static async Task ExpectSqlError(Func<Task> action, string sqlState, string error, string message)
{
    try { await action(); }
    catch (PostgresException ex) when (ex.SqlState == sqlState && ex.MessageText == error)
    {
        Console.WriteLine("PASS " + message);
        return;
    }
    throw new Exception("TEST_FAILURE: expected PostgreSQL error " + sqlState + "/" + error + " for " + message);
}

var alice = new SyntheticPrincipal("test:actor-alice", "test:member-alice", "test:tenant-A", "MEMBER_TEST", true);
var other = new SyntheticPrincipal("test:actor-bob", "test:member-bob", "test:tenant-A", "MEMBER_TEST", true);
var sponsor = new SyntheticPrincipal("test:actor-sponsor", "test:member-alice", "test:tenant-A", "SPONSOR_TEST", true);
Check(!SyntheticAccessPolicy.Evaluate(null, "test:member-alice", "test:tenant-A", "READ_OWN_TEST_PROFILE").Allowed, "ID-01 anonymous deny");
Check(!SyntheticAccessPolicy.Evaluate(alice, "test:member-bob", "test:tenant-A", "READ_OWN_TEST_PROFILE").Allowed, "ID-02 cross-member deny");
Check(SyntheticAccessPolicy.Evaluate(alice, "test:member-alice", "test:tenant-A", "READ_OWN_TEST_PROFILE").Allowed, "ID-03 scoped allow");
Check(!SyntheticAccessPolicy.Evaluate(new SyntheticPrincipal("test:forged", "test:member-alice", "test:tenant-A", "MEMBER_TEST", false),
    "test:member-alice", "test:tenant-A", "READ_OWN_TEST_PROFILE").Allowed, "ID-04 untrusted synthetic actor");
Check(!SyntheticAccessPolicy.Evaluate(alice, "test:member-alice", "test:tenant-B", "READ_OWN_TEST_PROFILE").Allowed, "ID-05 cross tenant deny");
Check(!SyntheticAccessPolicy.Evaluate(sponsor, "test:member-alice", "test:tenant-A", "READ_OWN_TEST_PROFILE").Allowed, "ID-06 sponsor deny");
Check(!SyntheticAccessPolicy.Evaluate(alice, "", "test:tenant-A", "READ_OWN_TEST_PROFILE").Allowed, "ID-07 missing evidence deny");
Check(!SyntheticAccessPolicy.Evaluate(alice, "test:member-alice", "test:tenant-A", "UNREVIEWED_ACTION").Allowed, "ID-08 unknown action deny");
Check(!SyntheticAccessPolicy.Evaluate(other, "test:member-alice", "test:tenant-A", "READ_OWN_TEST_CONSENT").Allowed, "X-07 sponsor/cross subject cannot impersonate");

var connectionString = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
    ?? throw new InvalidOperationException("TARANOM_TEST_PG must point only to isolated synthetic PostgreSQL");
await using var dataSource = NpgsqlDataSource.Create(connectionString);
var store = new SyntheticConsentStore(dataSource);
await store.InitializeTestSchemaAsync();
var subject = "test:member-alice";
var tenant = "test:tenant-A";
var scope = "test:private";
var research = SyntheticPurpose.SecondaryAiResearchTest;
var care = SyntheticPurpose.CareCommunicationTest;
Check(!await store.EvaluateAsync(alice, subject, tenant, research, scope), "C-05 missing research consent deny");
var first = await store.ChangeAsync(alice, subject, tenant, care, scope, 0, "test:request-care", true);
Check(first.Version == 1 && !first.Replayed, "C-03 care grant recorded");
Check(await store.EvaluateAsync(alice, subject, tenant, care, scope), "C-03 valid scoped care consent");
Check(!await store.EvaluateAsync(alice, subject, tenant, research, scope), "C-01 care does not imply AI research consent");
Check(!await store.EvaluateAsync(alice, subject, tenant, care, "test:other-scope"), "C-02 scope mismatch deny");
Check(!await store.EvaluateAsync(other, subject, tenant, care, scope), "C-02 subject mismatch deny");
var aliceTenantB = new SyntheticPrincipal(
    "test:actor-alice-b", "test:member-alice", "test:tenant-B", "MEMBER_TEST", true);
Check(!await store.EvaluateAsync(aliceTenantB, subject, "test:tenant-B", care, scope),
    "ID-05 identical member reference in another tenant has no inherited consent");
var tenantBGrant = await store.ChangeAsync(aliceTenantB, subject, "test:tenant-B",
    care, scope, 0, "test:tenant-b-care", true);
Check(tenantBGrant.Version == 1 &&
    await store.EvaluateAsync(aliceTenantB, subject, "test:tenant-B", care, scope),
    "ID-05 separate tenant starts independent consent stream");
Check(await store.EvaluateAsync(alice, subject, tenant, care, scope),
    "ID-05 original tenant consent unaffected by same-subject cross-tenant grant");
var tenantBWithdraw = await store.ChangeAsync(aliceTenantB, subject, "test:tenant-B",
    care, scope, 1, "test:tenant-b-withdraw", false);
Check(tenantBWithdraw.Version == 2 &&
    !await store.EvaluateAsync(aliceTenantB, subject, "test:tenant-B", care, scope),
    "ID-05 withdrawal in second tenant does not grant or revoke original tenant");
Check(await store.EvaluateAsync(alice, subject, tenant, care, scope),
    "ID-05 original tenant consent unaffected by cross-tenant withdrawal");

var countAfterGrant = await store.AuditCountAsync();
var replay = await store.ChangeAsync(alice, subject, tenant, care, scope, 0, "test:request-care", true);
Check(replay.Replayed && replay.Version == 1, "A-05 exact replay no duplicate");
Check(await store.AuditCountAsync() == countAfterGrant, "A-05 audit not duplicated");
await ExpectDenial(async () => { await store.ChangeAsync(alice, subject, tenant, care, scope, 0, "test:request-care", false); }, "IDEMPOTENCY_CONFLICT", "X-02 conflicting replay");
var withdrawn = await store.ChangeAsync(alice, subject, tenant, care, scope, 1, "test:request-withdraw", false);
Check(withdrawn.Version == 2, "C-04 withdrawal adds history");
Check(!await store.EvaluateAsync(alice, subject, tenant, care, scope), "C-04 post-withdrawal deny");
await ExpectDenial(async () => { await store.ChangeAsync(alice, subject, tenant, care, scope, 1, "test:request-stale", true); }, "VERSION_CONFLICT", "ID-07 stale version deny");
await ExpectDenial(async () => { await store.ChangeAsync(sponsor, subject, tenant, care, scope, 2, "test:request-sponsor", true); }, "DENY", "ID-06 sponsor cannot grant");
await ExpectDenial(async () => { await store.ChangeAsync(alice, subject, tenant, care, "", 2, "test:request-missing", true); }, "DENY", "C-05 missing scope denied");

// Parallel competing commands must serialize through a row lock, permitting only one version.
var win1 = store.ChangeAsync(alice, subject, tenant, care, scope, 2, "test:race-a", true);
var win2 = store.ChangeAsync(alice, subject, tenant, care, scope, 2, "test:race-b", true);
var outcomes = await Task.WhenAll(new[] { win1, win2 }.Select(async x =>
{
    try { await x; return true; }
    catch (InvalidOperationException e) when (e.Message == "VERSION_CONFLICT") { return false; }
}));
Check(outcomes.Count(x => x) == 1, "X-01 concurrent version race exactly one winner");
Check(await store.EvaluateAsync(alice, subject, tenant, care, scope), "X-05 fresh consent state visible");

await using (var cn = await dataSource.OpenConnectionAsync())
{
    await using var sql = new NpgsqlCommand("UPDATE synthetic_audit SET decision_code='ALTERED'", cn);
    await ExpectSqlError(async () => { await sql.ExecuteNonQueryAsync(); }, "P0001", "AUDIT_APPEND_ONLY", "A-02 X-04 DB audit update prevented");
}
await using (var cn = await dataSource.OpenConnectionAsync())
{
    await using var sql = new NpgsqlCommand("DELETE FROM synthetic_audit", cn);
    await ExpectSqlError(async () => { await sql.ExecuteNonQueryAsync(); }, "P0001", "AUDIT_APPEND_ONLY", "A-02 X-04 DB audit delete prevented");
}

// Failure injection: if audit persistence fails, the consent mutation and stream version must roll back.
var priorAuditCount = await store.AuditCountAsync();
await using (var cn = await dataSource.OpenConnectionAsync())
{
    await using var install = new NpgsqlCommand("""
        CREATE OR REPLACE FUNCTION synthetic_block_audit_inserts() RETURNS trigger
        LANGUAGE plpgsql AS 'BEGIN RAISE EXCEPTION ''SYNTHETIC_AUDIT_UNAVAILABLE''; END;';
        DROP TRIGGER IF EXISTS synthetic_test_audit_failure ON synthetic_audit;
        CREATE TRIGGER synthetic_test_audit_failure
            BEFORE INSERT ON synthetic_audit
            FOR EACH ROW EXECUTE FUNCTION synthetic_block_audit_inserts();
        """, cn);
    await install.ExecuteNonQueryAsync();
}
try
{
    await ExpectSqlError(async () =>
    {
        await store.ChangeAsync(alice, subject, tenant, care, scope, 3, "test:audit-failure", false);
    }, "P0001", "SYNTHETIC_AUDIT_UNAVAILABLE", "A-03 X-03 failed audit insertion rejects whole consent command");
}
finally
{
    await using var cn = await dataSource.OpenConnectionAsync();
    await using var cleanup = new NpgsqlCommand("""
        DROP TRIGGER IF EXISTS synthetic_test_audit_failure ON synthetic_audit;
        DROP FUNCTION IF EXISTS synthetic_block_audit_inserts();
        """, cn);
    await cleanup.ExecuteNonQueryAsync();
}
Check(await store.AuditCountAsync() == priorAuditCount, "A-03 audit failure creates no audit row");
Check(await store.EvaluateAsync(alice, subject, tenant, care, scope),
    "A-03 audit failure did not change consent state");
var afterFailure = await store.ChangeAsync(alice, subject, tenant, care, scope, 3, "test:post-audit-failure", false);
Check(afterFailure.Version == 4, "A-03 failed transaction did not advance version");
Check(!await store.EvaluateAsync(alice, subject, tenant, care, scope), "C-04 post-withdrawal still denied");
Check(!await store.EvaluateAsync(alice, subject, tenant, (SyntheticPurpose)12345, scope),
    "C-05 unknown consent purpose denied");
await ExpectDenial(async () =>
{
    await store.ChangeAsync(alice, subject, tenant, (SyntheticPurpose)12345, scope, 0,
        "test:unknown-purpose", true);
}, "INVALID_PURPOSE", "C-05 unknown purpose cannot grant");

// Evidence history, like audit records, is insert-only even when accessed through SQL.
await using (var cn = await dataSource.OpenConnectionAsync())
{
    await using var sql = new NpgsqlCommand("DELETE FROM synthetic_consent_evidence", cn);
    await ExpectSqlError(async () => { await sql.ExecuteNonQueryAsync(); }, "P0001", "AUDIT_APPEND_ONLY", "A-02 DB consent history delete rejected");
}

Console.WriteLine("PASS synthetic-only Sprint 01 checks completed");
