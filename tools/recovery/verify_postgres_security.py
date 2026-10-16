"""TRN-TECH-139 — deny-only ephemeral PostgreSQL restore security attestation.

No network or Stage access is permitted by this module; its SQL is used only
by the existing hard-gated ephemeral CI recovery rehearsal. Unlike rows,
security meaning includes RLS FORCE, policy roles and predicates, table ACLs,
function source/SECURITY DEFINER/config/ACLs and restricted-login posture.
"""
from __future__ import annotations

from typing import Callable
import json

MANDATORY_RLS = frozenset((
    "internal_test_org_draft_item",
    "internal_test_org_draft_audit",
    "internal_test_org_draft_activity",
))
MANDATORY_POLICIES = frozenset((
    "internal_test_org_own_read",
    "internal_test_org_own_insert",
    "internal_test_org_audit_own_read",
    "internal_test_org_activity_own_read",
))
PRIVILEGED_FUNCTIONS = frozenset((
    "internal_test_org_scope_for_login",
    "internal_test_org_audit_append",
    "internal_test_org_audit_immutable",
    "internal_test_org_activity_append",
    "internal_test_org_activity_immutable",
))
SCOPE_ROLES = frozenset((
    "taranom_scope_support_test",
    "taranom_scope_corporate_test",
))

# OID-free canonical rows are ordered before JSON serialization. Literal SQL
# uses no user input or dynamic database objects.
SECURITY_QUERIES = {
"tables": """
SELECT coalesce(jsonb_agg(jsonb_build_object(
 'name',c.relname,'rls',c.relrowsecurity,'force',c.relforcerowsecurity,
 'acl',(SELECT coalesce(jsonb_agg(jsonb_build_array(
   CASE WHEN x.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(x.grantee) END,
   pg_get_userbyid(x.grantor),x.privilege_type,x.is_grantable)
   ORDER BY x.grantee,x.grantor,x.privilege_type,x.is_grantable),'[]'::jsonb)
   FROM aclexplode(coalesce(c.relacl,acldefault('r',c.relowner))) x))
 ORDER BY c.relname)::text,'[]')
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname='public' AND c.relkind='r'
AND c.relname LIKE 'internal_test_%';
""",
"policies": """
SELECT coalesce(jsonb_agg(jsonb_build_object(
  'table',c.relname,'name',p.polname,'command',p.polcmd,
  'permissive',p.polpermissive,
  'roles',(SELECT coalesce(jsonb_agg(
    CASE WHEN x.role_oid=0 THEN 'PUBLIC' ELSE pg_get_userbyid(x.role_oid) END
    ORDER BY x.role_oid),'[]'::jsonb) FROM unnest(p.polroles) x(role_oid)),
  'using',pg_get_expr(p.polqual,p.polrelid),
  'check',pg_get_expr(p.polwithcheck,p.polrelid))
  ORDER BY c.relname,p.polname)::text,'[]')
FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname='public' AND c.relname LIKE 'internal_test_%';
""",
"functions": """
SELECT coalesce(jsonb_agg(jsonb_build_object(
 'name',p.proname,'args',pg_get_function_identity_arguments(p.oid),
 'definition',pg_get_functiondef(p.oid),
 'security_definer',p.prosecdef,'config',p.proconfig,
 'acl',(SELECT coalesce(jsonb_agg(jsonb_build_array(
   CASE WHEN x.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(x.grantee) END,
   pg_get_userbyid(x.grantor),x.privilege_type,x.is_grantable)
   ORDER BY x.grantee,x.grantor,x.privilege_type,x.is_grantable),'[]'::jsonb)
   FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) x),
 'public_execute',EXISTS(SELECT 1 FROM
   aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) x
   WHERE x.grantee=0 AND x.privilege_type='EXECUTE'))
 ORDER BY p.proname,pg_get_function_identity_arguments(p.oid))::text,'[]')
FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
WHERE n.nspname='public' AND p.proname LIKE 'internal_test_%';
""",
"scope_roles": """
SELECT coalesce(jsonb_agg(jsonb_build_object(
 'name',rolname,'login',rolcanlogin,'inherit',rolinherit,
 'superuser',rolsuper,'bypassrls',rolbypassrls,
 'createrole',rolcreaterole,'createdb',rolcreatedb)
 ORDER BY rolname)::text,'[]')
FROM pg_roles
WHERE rolname IN ('taranom_scope_support_test','taranom_scope_corporate_test');
""",
"effective_privileges": """
SELECT coalesce(jsonb_agg(jsonb_build_object(
 'role',r.rolname,'table',c.relname,
 'select',has_table_privilege(r.rolname,c.oid,'SELECT'),
 'insert',has_table_privilege(r.rolname,c.oid,'INSERT'),
 'update',has_table_privilege(r.rolname,c.oid,'UPDATE'),
 'delete',has_table_privilege(r.rolname,c.oid,'DELETE'),
 'truncate',has_table_privilege(r.rolname,c.oid,'TRUNCATE'))
 ORDER BY r.rolname,c.relname)::text,'[]')
FROM pg_roles r CROSS JOIN pg_class c
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE r.rolname IN ('taranom_scope_support_test','taranom_scope_corporate_test')
AND n.nspname='public' AND c.relkind='r'
AND c.relname LIKE 'internal_test_%';
""",
}

class SecurityMismatch(Exception):
    pass

def collect_security(query: Callable[[str], bytes]) -> dict:
    """Query an already hard-gated source/restore, never a supplied host."""
    result = {}
    for key, sql in SECURITY_QUERIES.items():
        try:
            value = json.loads(query(sql))
        except (ValueError, TypeError, UnicodeError) as exc:
            raise SecurityMismatch("RECOVERY_SECURITY_CATALOG_UNPARSABLE") from exc
        if not isinstance(value, list):
            raise SecurityMismatch("RECOVERY_SECURITY_CATALOG_NOT_ARRAY")
        result[key] = value
    validate_security(result)
    return result

def validate_security(data: dict) -> None:
    """Reject independently unsafe *source* AND restore, not only changes."""
    try:
        if set(data) != set(SECURITY_QUERIES):
            raise SecurityMismatch("RECOVERY_SECURITY_CATALOG_INCOMPLETE")
        tables = {v["name"]: v for v in data["tables"]}
        if len(tables) != len(data["tables"]) or not MANDATORY_RLS <= tables.keys():
            raise SecurityMismatch("RECOVERY_RLS_TABLES_MISSING")
        if any(tables[k]["rls"] is not True or
               tables[k]["force"] is not True for k in MANDATORY_RLS):
            raise SecurityMismatch("RECOVERY_RLS_FORCE_DISABLED")
        policies = [p["name"] for p in data["policies"]]
        if len(policies) != len(set(policies)) or not MANDATORY_POLICIES <= set(policies):
            raise SecurityMismatch("RECOVERY_RLS_POLICIES_MISSING")
        roles = {r["name"]: r for r in data["scope_roles"]}
        if len(roles) != 2 or set(roles) != SCOPE_ROLES:
            raise SecurityMismatch("RECOVERY_SCOPE_ROLES_MISSING")
        for role in roles.values():
            if (role["login"] is not True or
                role["inherit"] is not False or
                any(role[field] is not False for field in
                    ("superuser","bypassrls","createrole","createdb"))):
                raise SecurityMismatch("RECOVERY_SCOPE_ROLE_ESCALATED")
        functions = {f["name"] for f in data["functions"]}
        if not PRIVILEGED_FUNCTIONS <= functions:
            raise SecurityMismatch("RECOVERY_PRIVILEGED_FUNCTIONS_MISSING")
        for f in data["functions"]:
            if f["name"] in PRIVILEGED_FUNCTIONS and f["public_execute"] is not False:
                raise SecurityMismatch("RECOVERY_PUBLIC_FUNCTION_EXECUTE")
        for role in SCOPE_ROLES:
            rights = {v["table"]: v for v in data["effective_privileges"]
                      if v["role"] == role}
            if not MANDATORY_RLS <= rights.keys():
                raise SecurityMismatch("RECOVERY_SCOPE_PRIVILEGE_COVERAGE_MISSING")
            for table in MANDATORY_RLS:
                access = rights[table]
                if access["select"] is not True or any(access[k] is not False for
                    k in ("update","delete","truncate")):
                    raise SecurityMismatch("RECOVERY_SCOPE_RIGHTS_ESCALATED")
            # Appending is allowed only via SECURITY DEFINER command function.
            if (rights["internal_test_org_draft_audit"]["insert"] is not False or
                rights["internal_test_org_draft_activity"]["insert"] is not False):
                raise SecurityMismatch("RECOVERY_DIRECT_JOURNAL_INSERT")
    except (KeyError,TypeError,ValueError,AttributeError) as exc:
        raise SecurityMismatch("RECOVERY_SECURITY_CATALOG_MALFORMED") from exc

def attest_parity(original: dict, restored: dict) -> None:
    validate_security(original)
    validate_security(restored)
    if original != restored:
        raise SecurityMismatch("RECOVERY_SECURITY_POSTURE_DIFFERS")
