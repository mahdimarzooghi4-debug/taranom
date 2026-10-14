using Npgsql;

namespace Taranom.OrgScope.Foundation;

// TRN-TECH-131: test-only structural partitioning. Never an authenticated
// organization account, lawful delegation or entitlement to member data.
internal static class PgSyntheticOrganizationScope
{
    internal const string SupportRole = "taranom_scope_support_test";
    internal const string CorporateRole = "taranom_scope_corporate_test";
    internal const string SupportRef = "test:scope-support";
    internal const string CorporateRef = "test:scope-corporate";

    private static bool ValidSecret(string? password) =>
        password is { Length: >= 40 and <= 96 } &&
        password.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    internal static async Task InitializeAsync(
        NpgsqlDataSource owner, string? supportPassword, string? corporatePassword)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
            "CI_EPHEMERAL_ONLY" ||
            !ValidSecret(supportPassword) || !ValidSecret(corporatePassword) ||
            supportPassword == corporatePassword)
            throw new InvalidOperationException("ORG_SCOPE_TEST_ONLY_OR_CREDENTIALS_INVALID");

        await using var conn = await owner.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var target = new NpgsqlCommand("""
            SELECT current_database(),current_user,rolsuper
            FROM pg_roles WHERE rolname=current_user
            """,conn,tx))
        await using (var reader = await target.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync() ||
                reader.GetString(0) != "taranom_sprint64_center" ||
                reader.GetString(1) != "postgres" || !reader.GetBoolean(2))
                throw new InvalidOperationException("ORG_SCOPE_UNTRUSTED_SCHEMA_OWNER");
        }

        const string ddl = """
            DO $role$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles
                WHERE rolname='taranom_scope_support_test') THEN
                CREATE ROLE taranom_scope_support_test
                  LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE
                  NOREPLICATION NOBYPASSRLS;
              END IF;
              IF NOT EXISTS (SELECT 1 FROM pg_roles
                WHERE rolname='taranom_scope_corporate_test') THEN
                CREATE ROLE taranom_scope_corporate_test
                  LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE
                  NOREPLICATION NOBYPASSRLS;
              END IF;
            END $role$;
            CREATE TABLE IF NOT EXISTS public.internal_test_org_scope_binding (
              scope_ref text PRIMARY KEY
                 CHECK (scope_ref ~ '^test:[A-Za-z0-9_:-]{1,123}$'),
              market_id text NOT NULL CHECK
                 (market_id IN ('MARKET-SUPPORT','MARKET-CORPORATE')),
              login_role name NOT NULL UNIQUE
            );
            CREATE TABLE IF NOT EXISTS public.internal_test_org_draft_item (
              scope_ref text NOT NULL REFERENCES
                 public.internal_test_org_scope_binding(scope_ref),
              item_ref text NOT NULL CHECK
                 (item_ref ~ '^test:[A-Za-z0-9_:-]{1,123}$'),
              source_ref text NOT NULL CHECK
                 (source_ref ~ '^test:[A-Za-z0-9_:-]{1,123}$'),
              PRIMARY KEY (scope_ref,item_ref)
            );
            INSERT INTO public.internal_test_org_scope_binding
              (scope_ref,market_id,login_role)
            VALUES
              ('test:scope-support','MARKET-SUPPORT','taranom_scope_support_test'),
              ('test:scope-corporate','MARKET-CORPORATE','taranom_scope_corporate_test')
            ON CONFLICT (scope_ref) DO NOTHING;
            CREATE OR REPLACE FUNCTION public.internal_test_org_scope_for_login()
              RETURNS text LANGUAGE sql STABLE SECURITY DEFINER
              SET search_path = pg_catalog,public
              AS $fn$
                 SELECT scope_ref
                 FROM public.internal_test_org_scope_binding
                 WHERE login_role=session_user::name
              $fn$;
            REVOKE ALL ON FUNCTION public.internal_test_org_scope_for_login()
              FROM PUBLIC;
            REVOKE ALL ON TABLE public.internal_test_org_scope_binding
              FROM PUBLIC,taranom_scope_support_test,taranom_scope_corporate_test;
            REVOKE ALL ON TABLE public.internal_test_org_draft_item
              FROM PUBLIC,taranom_scope_support_test,taranom_scope_corporate_test;
            ALTER TABLE public.internal_test_org_draft_item ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.internal_test_org_draft_item FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS internal_test_org_own_read
              ON public.internal_test_org_draft_item;
            DROP POLICY IF EXISTS internal_test_org_own_insert
              ON public.internal_test_org_draft_item;
            CREATE POLICY internal_test_org_own_read
              ON public.internal_test_org_draft_item FOR SELECT
              TO taranom_scope_support_test,taranom_scope_corporate_test
              USING (scope_ref=public.internal_test_org_scope_for_login());
            CREATE POLICY internal_test_org_own_insert
              ON public.internal_test_org_draft_item FOR INSERT
              TO taranom_scope_support_test,taranom_scope_corporate_test
              WITH CHECK (scope_ref=public.internal_test_org_scope_for_login());
            GRANT USAGE ON SCHEMA public
              TO taranom_scope_support_test,taranom_scope_corporate_test;
            GRANT EXECUTE ON FUNCTION public.internal_test_org_scope_for_login()
              TO taranom_scope_support_test,taranom_scope_corporate_test;
            GRANT SELECT,INSERT ON TABLE public.internal_test_org_draft_item
              TO taranom_scope_support_test,taranom_scope_corporate_test;
            """;
        await using (var cmd = new NpgsqlCommand(ddl,conn,tx))
            await cmd.ExecuteNonQueryAsync();

        // Fixed role identifiers and secret alphabet are checked above:
        // escaping, free-form identifiers and passwords are never accepted.
        await using (var cmd = new NpgsqlCommand(
            "ALTER ROLE taranom_scope_support_test PASSWORD '" +
            supportPassword + "'",conn,tx))
            await cmd.ExecuteNonQueryAsync();
        await using (var cmd = new NpgsqlCommand(
            "ALTER ROLE taranom_scope_corporate_test PASSWORD '" +
            corporatePassword + "'",conn,tx))
            await cmd.ExecuteNonQueryAsync();

        // Fail closed if a pre-existing role binding was changed or injected.
        await using (var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM public.internal_test_org_scope_binding
            WHERE (scope_ref='test:scope-support'
                    AND market_id='MARKET-SUPPORT'
                    AND login_role='taranom_scope_support_test')
               OR (scope_ref='test:scope-corporate'
                    AND market_id='MARKET-CORPORATE'
                    AND login_role='taranom_scope_corporate_test')
            """,conn,tx))
        {
            if ((long)(await cmd.ExecuteScalarAsync() ?? -1L) != 2L)
                throw new InvalidOperationException("ORG_SCOPE_BINDING_CONFLICT");
        }
        await using (var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM public.internal_test_org_scope_binding
            """,conn,tx))
        {
            if ((long)(await cmd.ExecuteScalarAsync() ?? -1L) != 2L)
                throw new InvalidOperationException("ORG_SCOPE_EXTRA_BINDING");
        }
        await tx.CommitAsync();
    }

    internal static NpgsqlDataSource ScopedSource(
        NpgsqlConnectionStringBuilder admin, string role, string password)
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
            "CI_EPHEMERAL_ONLY" ||
            admin.Host is not ("127.0.0.1" or "localhost") ||
            admin.Port != 5432 || admin.Database != "taranom_sprint64_center" ||
            role is not (SupportRole or CorporateRole) || !ValidSecret(password))
            throw new InvalidOperationException("ORG_SCOPE_UNTRUSTED_LOGIN");
        var config = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Username = role, Password = password, Pooling = false
        };
        return NpgsqlDataSource.Create(config.ConnectionString);
    }
}
