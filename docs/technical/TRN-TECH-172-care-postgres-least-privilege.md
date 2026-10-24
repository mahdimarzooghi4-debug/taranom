# TRN-TECH-172 — DRAFT / PostgreSQL least-privilege for synthetic care-coordination HTTP

Date: 2026-10-18. This is a narrowly scoped local PostgreSQL test-runtime security boundary, not an approved healthcare/professional credential, operational deployment or Production service.

## Existing defect
TRN-TECH-115 isolated its event/audit case journal by table and synthetic IDs, but the local care HTTP Store was instantiated with the same PostgreSQL `NpgsqlDataSource` as its DDL/schema owner. A vulnerability in that endpoint would inherit the owner's table and DDL powers. The already implemented support journal has separately restricted runtime/reader roles, establishing the repository precedent.

## Bounded remediation
- Keep DDL/schema setup entirely on the CI-ephemeral owner connection, but issue `PgSyntheticCareCoordinationJournal.ApplyAsync` through a separate care **writer** role and `ReadAsync` through a distinct care **reader** role. Both are independent from owner and support/proofing roles. No new endpoints, case data, business state, action, or journal schema changes.
- PostgreSQL roles are created only when `TARANOM_CENTER_DEMO_TEST_ONLY=CI_EPHEMERAL_ONLY`, database is exactly `taranom_sprint64_center` and host/port are loopback 5432. Role passwords are independent cryptographically random 32-byte values generated at test startup; no literal secret, external provider or password log. Writer can SELECT/INSERT/UPDATE only case header and SELECT/INSERT only care event and audit; **no DELETE, no historical UPDATE**. Reader has SELECT only on the three care tables. Both have only schema USAGE and no account management privileges.
- Startup **must fail closed** if either role is actually schema owner, superuser, replication/role/database administrator, has CREATE on schema, receives inappropriate DML, or can SELECT support/proofing case data. Check role/table privileges as that principal and perform **actual denied SQL** (historical DELETE for writer, header UPDATE for reader, and cross-journal SELECT for each) expecting SQLSTATE 42501.
- Preserve original immutable journal, paired audit digest, transaction CAS, deterministic replay, read-only MVCC and `test:` reference constraints. Runtime exposes no role inspector, privileged connection or new public route. The writer and reader datasources are disposed with the host.

## Mandatory evidence
The existing full-stack Kestrel+PostgreSQL+Chromium care journey cannot boot if either role is overprivileged, cannot access the test database, or if denied SQL succeeds. Existing real care three-event/replay/race tests, all 14 canonical shared CI jobs, and exact-HEAD technical Code Review COMMENT are required. Passing tests are not independent human approval. No clinical services, real patients, lawful consent, financial/membership effects, Stage, Release or Production; national release `DENIED_BY_DEFAULT`.
