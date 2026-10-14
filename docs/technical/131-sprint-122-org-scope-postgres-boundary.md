# TRN-TECH-131 — Sprint 122: isolated organization partition foundation

State: **test-only internal PostgreSQL bounded-context foundation**, not identity, sponsor onboarding, authorization or Production. Stacked on Sprint 121, with previous PRs Draft/Open/unmerged.

## Problem and boundary
Taranom's accepted three audiences are people/families, supportive institutions, and corporate/organizational groups. The sponsor never owns an individual's medical record, membership or personal treatment credit. Do not invent member/organization contracts, delegated administrators, actual users, trusted OIDC/session issuers, insurer rights or payment authority. A new production tenant API is **not code-authorized** by this sprint.

This increment implements **database-level technical isolation** for two **synthetic** organization scopes corresponding only to `MARKET-SUPPORT` and `MARKET-CORPORATE`. Each can append a test-only opaque work-item reference and read only its own rows using independent PostgreSQL logins. It does not create members, import personally identifiable/clinical data, establish a legal organization, assign staff, issue a grant or launch a real portal.

## Exact tested invariant
- New isolated non-packable `.NET` assembly `Taranom.OrgScope.Foundation`, using PostgreSQL; no HTTP route, no frontend mock, no other bounded-context persistence dependency.
- CI-only schema owner runs on **loopback PostgreSQL** `taranom_sprint64_center` and creates `internal_test_org_scope_binding` (technical role-to-scope mapping, exactly two known synthetic fixtures) and `internal_test_org_draft_item` (scoped test metadata with composite key). Scope references and source references must be valid `test:` forms; no live data.
- Each of two independently authenticated PostgreSQL roles is `NOINHERIT`, non-superuser, non-createdb, non-createrole and `NOBYPASSRLS`; credentials are freshly minted within the existing isolated full CI job and never committed.
- Enforce **FORCE ROW LEVEL SECURITY** with SELECT and INSERT policies keyed to a SQL SECURITY DEFINER function that resolves `session_user` against the owner-controlled private binding table. A browser-supplied `role`, `scope`, header or any SQL session variable is not authority.
- Neither role may directly read the binding table, issue UPDATE/DELETE/TRUNCATE/DDL, grant privileges or `SET ROLE` to the other. These tests are performed through real PostgreSQL connections, not mocked permissions. The same opaque item reference may exist independently in two scopes without collision.
- This is a *structural* privacy invariant, not proof of lawful access: without an independently approved authentication issuer, verified organization owner, server-side current delegation, revocation, purpose and audit contract, **no production HTTP route or real-member data flow is permissible**.

## Acceptance
The standalone `Taranom.OrgScope.Probe` tests two real database logins, proves both can append and see exactly their own test item, confirms cross-scope read is empty in both directions, and verifies ten hostile direct SQL attempts are denied. It runs inside the existing 14-job full regression, alongside the inherited React/.NET/PostgreSQL source-backed tests and Stage-like recovery rehearsal. Passed CI is not QA Gate or release authority.

## Explicit exclusions
No real IdP, no browser-selected tenant context, no actual organization registration or business activation, no provider or financial API, no Production migrations, no Stage/QA/Release/Production, no national release. `DENIED_BY_DEFAULT` remains binding.
