# TRN-TECH-174 — DRAFT / synthetic late-evidence role isolation and nonlocking consistent reads
Date: 2026-10-18. Technical draft only, no release or legal claims.

## Defect and existing source
TRN-TECH-114's synthetic historic-period unverified evidence work-item journal has a canonical append-only event, paired audit, deterministic case revision and manual human-note lineage. Before Sprint 165, the HTTP read and write paths inherited the same schema-owner PostgreSQL datasource used to create tables and append-only triggers, and ordinary GET took the same FOR UPDATE case-row lock as a writer.

## Implementation
- Keep schema DDL as a CI-only privileged startup operation. The HTTP handler instead receives **two separate non-inheriting PostgreSQL users**: a bounded writer and SELECT-only reader. Random 32-byte hex secrets are created separately per test run, not stored in repo or browser. Configuration is fail-closed unless `TARANOM_CENTER_DEMO_TEST_ONLY=CI_EPHEMERAL_ONLY`, database exactly `taranom_sprint64_center`, host localhost/127.0.0.1, port 5432.
- Writer may SELECT/INSERT/UPDATE the case header and SELECT/INSERT event and paired-audit tables; reader may SELECT all three and has **no DML**, no schema CREATE, no role/admin powers. Neither principal may DELETE or UPDATE event/audit history nor SELECT proofing, care coordination or support case tables. Startup attests the actual current PostgreSQL role and every relevant privilege, then performs **negative SQL as the real principal**, requiring SQLSTATE 42501 for writer history DELETE, reader case UPDATE and cross-context SELECT.
- Reader reconstructs the exact same append-only, audit-digest verified event history within a PostgreSQL `REPEATABLE READ` / `SET TRANSACTION READ ONLY` snapshot, **without FOR UPDATE**. Writers retain case locking, original transaction CAS, replay scope and paired audit atomicity. No data migration, event semantics, outcome adjudication, or new API.
- Existing standalone proofing/late-evidence integration project now uses a separate reader fixture account with SELECT-only privileges, rather than passing writer datasource for both arguments. Add a real reader denial test; keep all existing replay, parallel CAS, historical corruption and simulated failed audit tests.

## Hard gates
Same full 14-job shared CI with actual .NET/PostgreSQL/Chromium and isolated restored database exercise, exact-head technical review COMMENT. This is an isolated developer/demo security control; no independent human approval or real identity, financial entitlement, verified reviewer, provider, medical or production source is implied. First official nationwide three-market launch remains `DENIED_BY_DEFAULT`. No Ready for Review, Merge, Stage, QA Gate, Release Approval or Production.
