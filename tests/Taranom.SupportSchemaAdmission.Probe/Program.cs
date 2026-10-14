using Npgsql;
using Taranom.CenterWorkflow.Demo;

// TRN-TECH-130: genuine PostgreSQL, ephemeral-only structural admission test.
if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
    "CI_EPHEMERAL_ONLY")
    throw new InvalidOperationException("SCHEMA_PROBE_TEST_ONLY");
var raw = Environment.GetEnvironmentVariable("TARANOM_TEST_PG")
    ?? throw new InvalidOperationException("SCHEMA_PROBE_DATABASE_MISSING");
var cfg = new NpgsqlConnectionStringBuilder(raw);
if (cfg.Host is not ("127.0.0.1" or "localhost") ||
    cfg.Port != 5432 || cfg.Database != "taranom_sprint64_center" ||
    cfg.Username != "postgres")
    throw new InvalidOperationException("SCHEMA_PROBE_UNTRUSTED_DATABASE");
await using var source = NpgsqlDataSource.Create(cfg.ConnectionString);

// Startup admission and replay are idempotent, not a new schema migration.
await PgSyntheticSupportJournal.InitializeAsync(source);
await Task.WhenAll(Enumerable.Range(0,4).Select(
    _ => PgSyntheticSupportJournal.InitializeAsync(source)));

await using var connection = await source.OpenConnectionAsync();
async Task Exec(string sql)
{
    await using var cmd = new NpgsqlCommand(sql,connection);
    await cmd.ExecuteNonQueryAsync();
}
async Task<string> Scalar(string sql)
{
    await using var cmd = new NpgsqlCommand(sql,connection);
    return (string)(await cmd.ExecuteScalarAsync() ??
        throw new InvalidOperationException("SCHEMA_PROBE_UNAVAILABLE"));
}
var oldDdl = await Scalar("""
    SELECT ddl_sha256 FROM internal_test_support_schema_admission
    WHERE schema_version=1
    """);
var oldCatalog = await Scalar("""
    SELECT catalog_sha256 FROM internal_test_support_schema_admission
    WHERE schema_version=1
    """);
if (oldDdl.Length != 64 || oldCatalog.Length != 64)
    throw new InvalidOperationException("SCHEMA_PROBE_NO_BASELINE");
async Task MustRejectDrift(string code)
{
    try
    {
        await PgSyntheticSupportJournal.InitializeAsync(source);
    }
    catch (InvalidOperationException ex)
        when (ex.Message == "SUPPORT_SCHEMA_DRIFT_OR_UNKNOWN_VERSION")
    {
        return;
    }
    throw new InvalidOperationException("SCHEMA_PROBE_ACCEPTED_" + code);
}

// Deliberately mutate then restore the ephemeral schema. This verifies
// admission detects a *real committed* PostgreSQL drift, not a mock.
await Exec("ALTER TABLE internal_test_support_case ADD COLUMN intruder text");
try { await MustRejectDrift("COLUMN"); }
finally { await Exec("ALTER TABLE internal_test_support_case DROP COLUMN intruder"); }
await PgSyntheticSupportJournal.InitializeAsync(source);

// Unknown/changed manifest must not be silently rewritten by startup.
await Exec("""
    UPDATE internal_test_support_schema_admission
    SET ddl_sha256=repeat('0',64) WHERE schema_version=1
    """);
try { await MustRejectDrift("DIGEST"); }
finally
{
    await using var restore = new NpgsqlCommand("""
        UPDATE internal_test_support_schema_admission
        SET ddl_sha256=@original WHERE schema_version=1
        """,connection);
    restore.Parameters.AddWithValue("original",oldDdl);
    await restore.ExecuteNonQueryAsync();
}
await PgSyntheticSupportJournal.InitializeAsync(source);

// A later incompatible version must not be backfilled without a new
// explicit, reviewed migration contract.
try
{
    await Exec("""
        INSERT INTO internal_test_support_schema_admission
            (schema_version,ddl_sha256,catalog_sha256)
        VALUES (2,repeat('A',64),repeat('B',64))
        """);
    throw new InvalidOperationException("SCHEMA_PROBE_UNAUTHORIZED_V2");
}
catch (PostgresException ex) when (ex.SqlState == "23514") { }

var current = await Scalar("""
    SELECT catalog_sha256 FROM internal_test_support_schema_admission
    WHERE schema_version=1
    """);
if (current != oldCatalog)
    throw new InvalidOperationException("SCHEMA_PROBE_NOT_RESTORED");
await PgSyntheticSupportJournal.InitializeAsync(source);
Console.WriteLine("PASS: PostgreSQL schema version admission, concurrency, drift, unknown-version denial and recovery");
