using Npgsql;

namespace Taranom.OrgScope.Foundation;

// TRN-TECH-134: versioned, append-only technical activity for a synthetic
// organization draft. Database login fixes scope; this is NOT staff authority.
internal static class OrgDraftActivitySchema
{
    internal static async Task EnsureAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS public.internal_test_org_draft_activity (
              scope_ref text NOT NULL,
              item_ref text NOT NULL,
              activity_no integer NOT NULL CHECK (activity_no > 0),
              request_ref text NOT NULL CHECK
                (request_ref ~ '^test:[A-Za-z0-9_:-]{1,123}$'),
              note_ref text NOT NULL CHECK
                (note_ref ~ '^test:[A-Za-z0-9_:-]{1,123}$'),
              expected_previous_no integer NOT NULL CHECK
                (expected_previous_no >= 0
                 AND activity_no = expected_previous_no + 1),
              previous_sha256 text NOT NULL CHECK
                (previous_sha256 = 'GENESIS'
                 OR previous_sha256 ~ '^[0-9A-F]{64}$'),
              activity_sha256 text NOT NULL CHECK
                (activity_sha256 ~ '^[0-9A-F]{64}$'),
              PRIMARY KEY (scope_ref,item_ref,activity_no),
              UNIQUE (scope_ref,item_ref,request_ref),
              FOREIGN KEY (scope_ref,item_ref)
                REFERENCES public.internal_test_org_draft_item(scope_ref,item_ref)
            );
            CREATE OR REPLACE FUNCTION public.internal_test_org_activity_append(
              p_item text, p_request text, p_note text, p_expected integer)
            RETURNS TABLE(result_code text, current_no integer)
            LANGUAGE plpgsql SECURITY DEFINER
            SET search_path = pg_catalog,public
            AS $append$
            DECLARE
              v_scope text;
              v_source text;
              v_existing_no integer;
              v_existing_note text;
              v_existing_expected integer;
              v_latest_no integer;
              v_previous text;
              v_digest text;
            BEGIN
              IF session_user::text NOT IN
                ('taranom_scope_support_test','taranom_scope_corporate_test')
              THEN
                RAISE EXCEPTION 'ORG_ACTIVITY_UNTRUSTED_LOGIN'
                  USING ERRCODE='42501';
              END IF;
              SELECT scope_ref INTO v_scope
                FROM public.internal_test_org_scope_binding
                WHERE login_role=session_user::name;
              IF v_scope IS NULL
                OR p_item IS NULL
                OR p_request IS NULL
                OR p_note IS NULL
                OR p_item !~ '^test:[A-Za-z0-9_:-]{1,123}$'
                OR p_request !~ '^test:[A-Za-z0-9_:-]{1,123}$'
                OR p_note !~ '^test:[A-Za-z0-9_:-]{1,123}$'
                OR p_expected IS NULL OR p_expected < 0
                OR p_expected = 2147483647
              THEN
                RAISE EXCEPTION 'ORG_ACTIVITY_INVALID_INPUT'
                  USING ERRCODE='22023';
              END IF;
              -- Serialize writers of the same organization/item, regardless of
              -- their connection. Hash collisions only add contention.
              PERFORM pg_advisory_xact_lock(
                hashtextextended(v_scope || '|' || p_item, 0));
              SELECT d.source_ref INTO v_source
                FROM public.internal_test_org_draft_item d
                WHERE d.scope_ref=v_scope AND d.item_ref=p_item;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'ORG_ACTIVITY_DRAFT_NOT_FOUND'
                  USING ERRCODE='42501';
              END IF;
              -- Even a direct SQL function caller may not append to a source
              -- whose immutable CREATE audit no longer matches.
              IF NOT EXISTS (
                SELECT 1 FROM public.internal_test_org_draft_audit a
                WHERE a.scope_ref=v_scope AND a.item_ref=p_item
                  AND a.event_no=1
                  AND a.event_type='DRAFT_RECORDED_TEST_ONLY'
                  AND a.source_ref=v_source
                  AND a.event_sha256=upper(encode(sha256(convert_to(
                    'TRN-TECH-133|' || v_scope || '|' || p_item || '|'
                    || v_source || '|DRAFT_RECORDED_TEST_ONLY|1',
                    'UTF8')),'hex'))
              ) THEN
                RAISE EXCEPTION 'ORG_ACTIVITY_SOURCE_AUDIT_MISMATCH'
                  USING ERRCODE='23514';
              END IF;
              SELECT a.activity_no,a.note_ref,a.expected_previous_no
                INTO v_existing_no,v_existing_note,v_existing_expected
                FROM public.internal_test_org_draft_activity a
                WHERE a.scope_ref=v_scope AND a.item_ref=p_item
                  AND a.request_ref=p_request;
              IF FOUND THEN
                IF v_existing_note=p_note
                  AND v_existing_expected=p_expected
                THEN
                  RETURN QUERY SELECT 'Replayed'::text,v_existing_no;
                ELSE
                  RETURN QUERY SELECT 'ConflictingReplay'::text,v_existing_no;
                END IF;
                RETURN;
              END IF;
              SELECT coalesce(max(a.activity_no),0) INTO v_latest_no
                FROM public.internal_test_org_draft_activity a
                WHERE a.scope_ref=v_scope AND a.item_ref=p_item;
              IF v_latest_no<>p_expected THEN
                RETURN QUERY SELECT 'StaleRevision'::text,v_latest_no;
                RETURN;
              END IF;
              v_previous := 'GENESIS';
              IF v_latest_no>0 THEN
                SELECT a.activity_sha256 INTO v_previous
                  FROM public.internal_test_org_draft_activity a
                  WHERE a.scope_ref=v_scope AND a.item_ref=p_item
                    AND a.activity_no=v_latest_no;
                IF v_previous IS NULL THEN
                  RAISE EXCEPTION 'ORG_ACTIVITY_BROKEN_CHAIN'
                    USING ERRCODE='23514';
                END IF;
              END IF;
              v_digest := upper(encode(sha256(convert_to(
                'TRN-TECH-134|' || v_scope || '|' || p_item || '|'
                || (v_latest_no+1)::text || '|' || p_request || '|'
                || p_note || '|' || v_previous, 'UTF8')),'hex'));
              INSERT INTO public.internal_test_org_draft_activity
                (scope_ref,item_ref,activity_no,request_ref,note_ref,
                 expected_previous_no,previous_sha256,activity_sha256)
              VALUES (v_scope,p_item,v_latest_no+1,p_request,p_note,
                      v_latest_no,v_previous,v_digest);
              RETURN QUERY SELECT 'Recorded'::text,v_latest_no+1;
            END;
            $append$;
            CREATE OR REPLACE FUNCTION public.internal_test_org_activity_immutable()
            RETURNS trigger LANGUAGE plpgsql
            SET search_path = pg_catalog,public
            AS $guard$
            BEGIN
              RAISE EXCEPTION 'ORG_ACTIVITY_APPEND_ONLY' USING ERRCODE='42501';
            END;
            $guard$;
            DROP TRIGGER IF EXISTS internal_test_org_activity_no_mutation
              ON public.internal_test_org_draft_activity;
            CREATE TRIGGER internal_test_org_activity_no_mutation
              BEFORE UPDATE OR DELETE
              ON public.internal_test_org_draft_activity
              FOR EACH ROW EXECUTE FUNCTION
                public.internal_test_org_activity_immutable();
            DROP TRIGGER IF EXISTS internal_test_org_activity_no_truncate
              ON public.internal_test_org_draft_activity;
            CREATE TRIGGER internal_test_org_activity_no_truncate
              BEFORE TRUNCATE ON public.internal_test_org_draft_activity
              FOR EACH STATEMENT EXECUTE FUNCTION
                public.internal_test_org_activity_immutable();
            ALTER TABLE public.internal_test_org_draft_activity
              ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.internal_test_org_draft_activity
              FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS internal_test_org_activity_own_read
              ON public.internal_test_org_draft_activity;
            CREATE POLICY internal_test_org_activity_own_read
              ON public.internal_test_org_draft_activity
              FOR SELECT TO taranom_scope_support_test,
                            taranom_scope_corporate_test
              USING (scope_ref=public.internal_test_org_scope_for_login());
            REVOKE ALL ON TABLE public.internal_test_org_draft_activity
              FROM PUBLIC,taranom_scope_support_test,taranom_scope_corporate_test;
            REVOKE ALL ON FUNCTION public.internal_test_org_activity_append(
              text,text,text,integer)
              FROM PUBLIC,taranom_scope_support_test,taranom_scope_corporate_test;
            REVOKE ALL ON FUNCTION public.internal_test_org_activity_immutable()
              FROM PUBLIC,taranom_scope_support_test,taranom_scope_corporate_test;
            GRANT SELECT ON TABLE public.internal_test_org_draft_activity
              TO taranom_scope_support_test,taranom_scope_corporate_test;
            GRANT EXECUTE ON FUNCTION public.internal_test_org_activity_append(
              text,text,text,integer)
              TO taranom_scope_support_test,taranom_scope_corporate_test;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync();
    }
}
