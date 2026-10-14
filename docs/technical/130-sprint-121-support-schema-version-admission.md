# TRN-TECH-130 — Sprint 121 PostgreSQL schema admission foundation

**Scope:** .NET internal execution infrastructure on an isolated ephemeral PostgreSQL fixture, without external APIs, real identities, patient information, payments or formal release.

The existing test-only support event/audit journal remains the sole source of truth. Sprint 121 changes its startup schema initialization from unconditional `CREATE TABLE IF NOT EXISTS` into **versioned admission**.

## Implemented invariants

1. Only the explicit `CI_EPHEMERAL_ONLY` host and PostgreSQL `taranom_sprint64_center` with the trusted schema-owner account may initialize or recheck the schema; the already separated HTTP writer and SELECT-only reader receive **no migration privileges**.
2. A transaction-wide PostgreSQL advisory lock serializes startup contenders, and version 1 uses an immutable DDL digest bound to a live catalog fingerprint: table columns, defaults, not-null, constraints, indexes, explicit triggers and append-only guard function body.
3. An unversioned pre-existing support schema cannot be silently adopted; unknown version, deleted manifest, modified migration text or post-initialization DDL drift **fails closed**. A check never silently repairs/manipulates source tables.
4. The first initialization executes the already accepted support SQL **once**, inserts its catalog and DDL fingerprint in the same DB transaction, and commits atomically. Further initializations only verify them.
5. The manifest is not exposed via a new HTTP endpoint; it conveys only technical schema evidence and creates no Business outcomes, permissions or trusted evidence of actual membership.

## Mandatory real PostgreSQL testing

A separate friend-test-only .NET console probe executes four concurrent schema admissions and verifies serial/idempotent behavior, commits an unauthorized column mutation and confirms denial, removes that mutation and confirms recovery, tampers with the DDL digest and confirms denial/recovery, rejects an unknown schema version and rechecks the preserved catalog signature. The test runs within the existing full shared 14-job CI alongside source-backed browser and HTTP regressions and PostgreSQL backup/restore.

## Not yet authorized

This is an **admission foundation**, not a generalized Production schema migration engine or permission to run DDL against operational databases. Adding a version 2 migration requires separately reviewed immutable SQL, a forward-only/rollback-recovery strategy and migration audit. No Production deployment, actual account/tenant provisioning, external API, Stage, formal QA Gate, Release Approval or national launch is authorized. Formal release remains `DENIED_BY_DEFAULT`.
