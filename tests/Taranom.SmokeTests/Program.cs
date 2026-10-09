using Npgsql;
using Taranom.SyntheticFoundation;

static void Check(bool expression, string message)
{
    if (!expression) throw new Exception("TEST_FAILURE: " + message);
    Console.WriteLine("PASS " + message);
}

static async Task ExpectError(Func<Task> action, string message)
{
    try { await action(); }
    catch { Console.WriteLine("PASS " + message); return; }
    throw new Exception("TEST_FAILURE: expected rejection " + message);
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
var countAfterGrant = await store.AuditCountAsync();
var replay = await store.ChangeAsync(alice, subject, tenant, care, scope, 0, "test:request-care", true);
Check(replay.Replayed && replay.Version == 1, "A-05 exact replay no duplicate");
Check(await store.AuditCountAsync() == countAfterGrant, "A-05 audit not duplicated");
await ExpectError(async () => { await store.ChangeAsync(alice, subject, tenant, care, scope, 0, "test:request-care", false); }, "X-02 conflicting replay");
var withdrawn = await store.ChangeAsync(alice, subject, tenant, care, scope, 1, "test:request-withdraw", false);
Check(withdrawn.Version == 2, "C-04 withdrawal adds history");
Check(!await store.EvaluateAsync(alice, subject, tenant, care, scope), "C-04 post-withdrawal deny");
await ExpectError(async () => { await store.ChangeAsync(alice, subject, tenant, care, scope, 1, "test:request-stale", true); }, "ID-07 stale version deny");
await ExpectError(async () => { await store.ChangeAsync(sponsor, subject, tenant, care, scope, 2, "test:request-sponsor", true); }, "ID-06 sponsor cannot grant");
await ExpectError(async () => { await store.ChangeAsync(alice, subject, tenant, care, "", 2, "test:request-missing", true); }, "C-05 missing scope denied");

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
    await ExpectError(async () => { await sql.ExecuteNonQueryAsync(); }, "A-02 X-04 DB audit update prevented");
}
await using (var cn = await dataSource.OpenConnectionAsync())
{
    await using var sql = new NpgsqlCommand("DELETE FROM synthetic_audit", cn);
    await ExpectError(async () => { await sql.ExecuteNonQueryAsync(); }, "A-02 X-04 DB audit delete prevented");
}
Console.WriteLine("PASS synthetic-only Sprint 01 checks completed");
