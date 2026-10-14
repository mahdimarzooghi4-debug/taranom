using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-130: TEST-ONLY, deterministic PostgreSQL schema admission.
// This is NOT a Production migration system or authorization to use real data.
internal static class SupportSchemaAdmission
{
    internal const int CurrentVersion = 1;
    private const long AdvisoryLock = 7812012101;

    private const string AdmissionTableSql = """
        CREATE TABLE IF NOT EXISTS internal_test_support_schema_admission (
            schema_version integer PRIMARY KEY CHECK (schema_version = 1),
            ddl_sha256 text NOT NULL CHECK (ddl_sha256 ~ '^[0-9A-F]{64}$'),
            catalog_sha256 text NOT NULL CHECK (catalog_sha256 ~ '^[0-9A-F]{64}$')
        )
        """;

    // The checked items are explicit, SQL-owner controlled structural facts.
    // Never hash volatile PostgreSQL OIDs or timestamps into the manifest.
    private const string CatalogSql = """
        WITH actual AS (
            SELECT 'COLUMN|' || c.relname || '|' || a.attnum::text || '|' ||
                   a.attname || '|' || format_type(a.atttypid,a.atttypmod) || '|' ||
                   a.attnotnull::text || '|' || COALESCE(pg_get_expr(d.adbin,d.adrelid),'')
                   AS part
            FROM pg_class c
            JOIN pg_namespace n ON n.oid=c.relnamespace
            JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0
               AND NOT a.attisdropped
            LEFT JOIN pg_attrdef d ON d.adrelid=c.oid AND d.adnum=a.attnum
            WHERE n.nspname='public' AND c.relkind='r' AND
                  c.relname IN ('internal_test_support_case',
                                 'internal_test_support_event',
                                 'internal_test_support_audit')
            UNION ALL
            SELECT 'CONSTRAINT|' || c.relname || '|' || p.conname || '|' ||
                   pg_get_constraintdef(p.oid)
            FROM pg_constraint p
            JOIN pg_class c ON c.oid=p.conrelid
            JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relname IN
                ('internal_test_support_case', 'internal_test_support_event',
                 'internal_test_support_audit')
            UNION ALL
            SELECT 'TRIGGER|' || c.relname || '|' || t.tgname || '|' ||
                   pg_get_triggerdef(t.oid)
            FROM pg_trigger t
            JOIN pg_class c ON c.oid=t.tgrelid
            JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE NOT t.tgisinternal AND n.nspname='public'
              AND c.relname IN ('internal_test_support_case',
                                'internal_test_support_event',
                                'internal_test_support_audit')
        )
        SELECT COALESCE(string_agg(part,E'\n' ORDER BY part COLLATE "C"),'')
        FROM actual
        """;

    internal static async Task EnsureAsync(NpgsqlDataSource owner, string versionOneSql)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
            "CI_EPHEMERAL_ONLY")
            throw new InvalidOperationException("SUPPORT_SCHEMA_ADMISSION_TEST_ONLY");
        if (string.IsNullOrWhiteSpace(versionOneSql))
            throw new InvalidOperationException("SUPPORT_SCHEMA_EMPTY_VERSION");

        await using var conn = await owner.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var authority = new NpgsqlCommand(
            "SELECT current_database(),current_user,rolsuper FROM pg_roles WHERE rolname=current_user",
            conn,tx))
        await using (var row = await authority.ExecuteReaderAsync())
        {
            if (!await row.ReadAsync() || row.GetString(0) != "taranom_sprint64_center" ||
                !row.GetBoolean(2))
                throw new InvalidOperationException("SUPPORT_SCHEMA_OWNER_UNTRUSTED");
        }
        await using (var lockCmd = new NpgsqlCommand(
            $"SELECT pg_advisory_xact_lock({AdvisoryLock})",conn,tx))
            await lockCmd.ExecuteNonQueryAsync();

        // Before introducing the admission table, refuse an unknown/preexisting
        // unversioned schema. This must never silently adopt arbitrary DDL.
        var hasManifest = await ScalarLongAsync(conn,tx,"""
            SELECT count(*) FROM pg_class c JOIN pg_namespace n
              ON n.oid=c.relnamespace
            WHERE c.relkind='r' AND n.nspname='public'
              AND c.relname='internal_test_support_schema_admission'
            """) == 1;
        if (!hasManifest && await ScalarLongAsync(conn,tx,"""
            SELECT count(*) FROM pg_class c JOIN pg_namespace n
              ON n.oid=c.relnamespace
            WHERE c.relkind='r' AND n.nspname='public'
              AND c.relname IN ('internal_test_support_case',
                'internal_test_support_event','internal_test_support_audit')
            """) != 0)
            throw new InvalidOperationException("SUPPORT_SCHEMA_UNVERSIONED_LEGACY");

        await using (var create = new NpgsqlCommand(AdmissionTableSql,conn,tx))
            await create.ExecuteNonQueryAsync();

        var records = new List<(int Version,string DdlHash,string CatalogHash)>();
        await using (var query = new NpgsqlCommand("""
            SELECT schema_version,ddl_sha256,catalog_sha256
            FROM internal_test_support_schema_admission
            ORDER BY schema_version
            """,conn,tx))
        await using (var reader = await query.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                records.Add((reader.GetInt32(0),reader.GetString(1),reader.GetString(2)));
        }

        var expectedDdl = Digest(versionOneSql);
        if (records.Count == 0)
        {
            if (hasManifest)
                throw new InvalidOperationException("SUPPORT_SCHEMA_MANIFEST_ERASED");
            await using (var bootstrap = new NpgsqlCommand(versionOneSql,conn,tx))
                await bootstrap.ExecuteNonQueryAsync();
            var actual = await CatalogDigestAsync(conn,tx);
            if (actual == Digest(string.Empty))
                throw new InvalidOperationException("SUPPORT_SCHEMA_EMPTY_CATALOG");
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO internal_test_support_schema_admission
                    (schema_version,ddl_sha256,catalog_sha256)
                VALUES (1,@ddl,@catalog)
                """,conn,tx))
            {
                insert.Parameters.AddWithValue("ddl",expectedDdl);
                insert.Parameters.AddWithValue("catalog",actual);
                await insert.ExecuteNonQueryAsync();
            }
        }
        else
        {
            if (records.Count != 1 || records[0].Version != CurrentVersion ||
                records[0].DdlHash != expectedDdl ||
                records[0].CatalogHash != await CatalogDigestAsync(conn,tx))
                throw new InvalidOperationException("SUPPORT_SCHEMA_DRIFT_OR_UNKNOWN_VERSION");
        }
        await tx.CommitAsync();
    }

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection c, NpgsqlTransaction t, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql,c,t);
        return (long)(await cmd.ExecuteScalarAsync() ??
            throw new InvalidOperationException("SUPPORT_SCHEMA_QUERY_UNAVAILABLE"));
    }

    private static async Task<string> CatalogDigestAsync(
        NpgsqlConnection c,NpgsqlTransaction t)
    {
        await using var cmd = new NpgsqlCommand(CatalogSql,c,t);
        return Digest((string)(await cmd.ExecuteScalarAsync() ??
            throw new InvalidOperationException("SUPPORT_SCHEMA_CATALOG_UNAVAILABLE")));
    }
    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
