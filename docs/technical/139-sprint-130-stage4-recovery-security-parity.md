# TRN-TECH-139 — Sprint 130: Ephemeral restore privilege/RLS security parity

**Purpose:** remove a concrete pre-Stage4 recovery security blind spot. The previous CI-only `pg_dump --no-acl` and `pg_restore --no-acl` omitted access-control lists; restored functions can revert to default PUBLIC EXECUTE. Row/schema/trigger equality alone cannot certify the effective restrictions of a restored PostgreSQL journal.

## Bounded correction

This is ONLY the existing GitHub Actions ephemeral PostgreSQL 18.6 recovery rehearsal with fixed loopback source `taranom_sprint64_center` and disposable restore `taranom_sprint77_restore`. It cannot run on user-supplied hosts/DBs or official Stage 4. Dump/restore retain PostgreSQL object grants and REVOKEs while continuing with `--no-owner`; roles are pre-existing synthetic CI cluster roles, **not** portable Stage access rights. Private dump bytes remain in a temporary directory and never uploaded.

The same source and restored DB are independently queried for:
- all `internal_test_*` tables' RLS enable/FORCE and normalized ACL grants;
- named RLS policy commands, role OIDs resolved to role names, predicates and WITH CHECK;
- routine definitions (`pg_get_functiondef`), SECURITY DEFINER flags, per-function runtime config and effective ACLs;
- fixed synthetic role LOGIN/NOINHERIT/non-superuser/non-BYPASSRLS/non-CREATEROLE posture;
- per-table effective SELECT/INSERT/UPDATE/DELETE/TRUNCATE for the two scoped roles.

The verifier **denies unsafe source as well as restore**: three mandatory org draft/item/audit/activity tables require FORCE RLS, four mandatory policies must exist, source-bound privileged functions cannot be PUBLIC-executable, both scoped logins must have precisely restricted attributes, direct journal INSERT and UPDATE/DELETE/TRUNCATE are denied. After these checks, any difference across the two complete security catalogs aborts recovery. Source backup and restored DB are checked again after testing mutations. No successful result grants any Stage/Production admission.

## Technical acceptance and remaining gates

- Changed recovery path must pass **real** dump/restore under inherited 14-job CI, alongside immutable event checks, exact per-table record digests and PostgreSQL + Chromium suite. Negative Python contract tests cover stripped ACL flags, malformed metadata, missing RLS/roles and non-authorizing report semantics.
- `security_posture_equal=true` is an **ephemeral CI assertion only**; `stage4_recovery_verified=false`, `production_recovery_verified=false`, and `national_release_allowed=false` remain immutable. No real data, new identities, providers, insurance, bank, AI API, Release Approval or nationwide launch is authorized.
- External Stage4 target, independent review, explicit integration/merge authorization and Stage-specific runbook remain blocked under [Issue #142](https://github.com/mahdimarzooghi4-debug/taranom/issues/142).
