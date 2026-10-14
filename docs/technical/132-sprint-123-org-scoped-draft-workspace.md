# TRN-TECH-132 — Sprint 123 RLS-scoped organization draft workspace

**Status:** isolated technical workflow only, stacked Draft on Sprint 122. No legal organization onboarding, operator session, verified member, external API, clinical/financial effect, or public HTTP route.

## Authorized foundation
Reuse exactly the two approved technical fixture audiences (supportive institutions and corporate organizations) and the Sprint 122 PostgreSQL `FORCE RLS` policy with two independently credentialed database roles. Do not change the source of organization identity to a request parameter, cookie, browser role or mutable SQL session variable. The existing `internal_test_org_draft_item` table stores only opaque `test:` item/source refs, **no personal or clinical data**.

## New operationally meaningful technical path
The separate internal `PgScopedDraftWorkspace` (non-packable .NET bounded context) provides:

- **RecordAsync(itemRef, sourceRef)**: new draft metadata row using the database login's authoritative `session_user` scope, never a caller-supplied tenant. PostgreSQL `INSERT ... ON CONFLICT (scope_ref,item_ref) DO NOTHING` and a source re-read in the same transaction provide exactly one accepted create across concurrent connections; exact repeats return `Replayed`, a changed source under the same scope+item key returns `ConflictingReplay` with the original row unchanged. Raw identities, URLs, oversized/malformed refs are denied before SQL.
- **ReadAsync(itemRef)**: can only read that role's own row; cannot ask for a different scope.
- **ListAsync(afterItemRef,limit)**: bounded 1–20 keyset read, `C` collation, a single PostgreSQL `REPEATABLE READ / READ ONLY` snapshot and lookahead cursor. Again no scope argument; the database login binds scope. No patient/person/source authority is inferred.

No business status, financial ledger, enrollment transition or automatic authorization is created. This is a *draft metadata pathway* only, not a production service.

## Acceptance: genuine PostgreSQL, no mocks
The existing `Taranom.OrgScope.Probe` now exercises:
1. record → identical replay → conflicting replay without mutation;
2. same item reference in distinct SUPPORT and CORPORATE scopes, with distinct source refs;
3. reject non-`test:` input and malformed cursor;
4. eight concurrent attempts through separate RLS-restricted PostgreSQL connections, one `Recorded` and seven `Replayed`;
5. sorted keyset pages with exact next cursor and exhausted page;
6. read from each organization returns only its own drafts, including deliberate nonexistence checks across scopes;
7. attempts to use the postgres owner as an organization operator fail closed;
8. inherited 10 direct-SQL RLS/permission-denial attempts continue to pass.

Keep all tests in the full shared 14-job CI. First-class independent human review is still required; CI alone is not a formal QA Gate.

## Explicit exclusions and next prerequisites
No new HTTP routes, real identity session, organization administrator registration, member data, clinical permissions, financial services, external integrations, Stage, Release Approval, Production, or national launch. Before a user-facing organization workflow can be activated, verify independently trusted operator identity, organization ownership, delegation, revocation, audit and resource access rights. `DENIED_BY_DEFAULT` remains binding.
