using Npgsql;

namespace Taranom.OrgScope.Foundation;

// TRN-TECH-133: atomically append one technical CREATE observation per new
// synthetic draft through an owner-controlled trigger. This never authorizes
// an organization, its operator, or an operational member-data workflow.
internal static class OrgDraftAuditSchema
{
    internal const string EventType = "DRAFT_RECORDED_TEST_ONLY";

    internal static async Task EnsureAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        // No silent adoption of a previously populated unjournaled fixture.
        await using (var check = new NpgsqlCommand("""
            SELECT
                (SELECT count(*) FROM public.internal_test_org_draft_item),
                to_regclass('public.internal_test_org_draft_audit') IS NOT NULL
            """, connection, transaction))
        await using (var row = await check.ExecuteReaderAsync())
        {
            if (!await row.ReadAsync() ||
                (row.GetInt64(0) != 0 && !row.GetBoolean(1)))
                throw new InvalidOperationException("ORG_DRAFT_UNJOURNALED_LEGACY");
        }

        const string ddl = """
            CREATE TABLE IF NOT EXISTS public.internal_test_org_draft_audit (
              scope_ref text NOT NULL,
              item_ref text NOT NULL,
              event_no integer NOT NULL CHECK (event_no=1),
              event_type text NOT NULL CHECK
                (event_type='DRAFT_RECORDED_TEST_ONLY'),
              source_ref text NOT NULL CHECK
                (source_ref ~ '^test:[A-Za-z0-9_:-]{1,123}$'),
              event_sha256 text NOT NULL CHECK
                (event_sha256 ~ '^[0-9A-F]{64}$'),
              PRIMARY KEY (scope_ref,item_ref,event_no),
              FOREIGN KEY (scope_ref,item_ref)
                REFERENCES public.internal_test_org_draft_item(scope_ref,item_ref)
            );
            CREATE OR REPLACE FUNCTION public.internal_test_org_audit_append()
            RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
            SET search_path = pg_catalog,public
            AS $append$
            DECLARE accepted_scope text;
            BEGIN
              SELECT scope_ref INTO accepted_scope
                FROM public.internal_test_org_scope_binding
                WHERE login_role=session_user::name;
              IF accepted_scope IS NULL OR accepted_scope<>NEW.scope_ref THEN
                RAISE EXCEPTION 'ORG_DRAFT_AUDIT_SCOPE_DENIED'
                  USING ERRCODE='42501';
              END IF;
              INSERT INTO public.internal_test_org_draft_audit
                (scope_ref,item_ref,event_no,event_type,source_ref,event_sha256)
              VALUES (NEW.scope_ref,NEW.item_ref,1,
                'DRAFT_RECORDED_TEST_ONLY',NEW.source_ref,
                upper(encode(sha256(convert_to(
                    'TRN-TECH-133|' || NEW.scope_ref || '|' || NEW.item_ref
                    || '|' || NEW.source_ref
                    || '|DRAFT_RECORDED_TEST_ONLY|1','UTF8')),'hex')));
              RETURN NEW;
            END;
            $append$;
            CREATE OR REPLACE FUNCTION public.internal_test_org_audit_immutable()
            RETURNS trigger LANGUAGE plpgsql
            SET search_path = pg_catalog,public
            AS $guard$
            BEGIN
              RAISE EXCEPTION 'ORG_DRAFT_AUDIT_APPEND_ONLY'
                USING ERRCODE='42501';
            END;
            $guard$;
            DROP TRIGGER IF EXISTS internal_test_org_draft_after_insert
              ON public.internal_test_org_draft_item;
            CREATE TRIGGER internal_test_org_draft_after_insert
              AFTER INSERT ON public.internal_test_org_draft_item
              FOR EACH ROW EXECUTE FUNCTION
                public.internal_test_org_audit_append();
            DROP TRIGGER IF EXISTS internal_test_org_audit_no_mutation
              ON public.internal_test_org_draft_audit;
            CREATE TRIGGER internal_test_org_audit_no_mutation
              BEFORE UPDATE OR DELETE ON public.internal_test_org_draft_audit
              FOR EACH ROW EXECUTE FUNCTION
                public.internal_test_org_audit_immutable();
            DROP TRIGGER IF EXISTS internal_test_org_audit_no_truncate
              ON public.internal_test_org_draft_audit;
            CREATE TRIGGER internal_test_org_audit_no_truncate
              BEFORE TRUNCATE ON public.internal_test_org_draft_audit
              FOR EACH STATEMENT EXECUTE FUNCTION
                public.internal_test_org_audit_immutable();
            ALTER TABLE public.internal_test_org_draft_audit
              ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.internal_test_org_draft_audit
              FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS internal_test_org_audit_own_read
              ON public.internal_test_org_draft_audit;
            CREATE POLICY internal_test_org_audit_own_read
              ON public.internal_test_org_draft_audit
              FOR SELECT TO taranom_scope_support_test,
                            taranom_scope_corporate_test
              USING (scope_ref=public.internal_test_org_scope_for_login());
            REVOKE ALL ON TABLE public.internal_test_org_draft_audit
              FROM PUBLIC,taranom_scope_support_test,
                   taranom_scope_corporate_test;
            REVOKE ALL ON FUNCTION public.internal_test_org_audit_append()
              FROM PUBLIC,taranom_scope_support_test,
                   taranom_scope_corporate_test;
            REVOKE ALL ON FUNCTION public.internal_test_org_audit_immutable()
              FROM PUBLIC,taranom_scope_support_test,
                   taranom_scope_corporate_test;
            GRANT SELECT ON TABLE public.internal_test_org_draft_audit
              TO taranom_scope_support_test,taranom_scope_corporate_test;
            """;
        await using var cmd = new NpgsqlCommand(ddl, connection, transaction);
        await cmd.ExecuteNonQueryAsync();
    }
}
