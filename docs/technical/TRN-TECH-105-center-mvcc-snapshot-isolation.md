# TRN-TECH-105 — Read-only center MVCC snapshot isolation

**Date:** 2026-10-13. **Sprint:** 96. **Business:** TRN-BIZ-053/054 (single center, multiple departments, separately unverified doctors); TRN-BIZ-028/030/040 (comprehensive three-market national launch remains blocked). **Backlog:** TRN-EP-04, TRN-EP-12. **Inherited contracts:** TRN-TECH-073/074/102/103/104. **Parent:** Sprint95 Draft PR #106 exact `6ce631da74aa77b9caec3e971133f48d555ddbd7`, both full 14-job regressions green, preliminary COMMENT review recorded. This is a synthetic-only technical reliability change, not permission to operate a real center.

## Verified problem

`PgClinicalCenterJournal.ReadAsync` called the same `RehydrateLockedAsync` used by writers. That helper took `SELECT ... FOR UPDATE` against the center row **even for a GET**; another active writer transaction could block a historical/audit/roster read, and large read traffic could unnecessarily serialize writes. Simply removing `FOR UPDATE` under the default READ COMMITTED level would be unsafe: source version, events, paired audit and four relational projections could then come from *different* committed revisions.

## Contract: one consistent PostgreSQL MVCC read

- **Read side:** one `REPEATABLE READ`, `READ ONLY` PostgreSQL transaction per `ReadAsync`; plain `SELECT current_version` without writer row lock, followed by the *unchanged* complete journal digest/audit/reducer/relational-projection verification in the **same** transaction. Missing synthetic centers continue to return empty without materialization. Every invocation returns one consistent committed PostgreSQL snapshot; it need not be the latest commit at response time.
- **Write side:** retains original `READ COMMITTED` transaction, `SELECT ... FOR UPDATE` center serialization, exact expected-version CAS, append-only paired audit, idempotency and rollback. No new writer pathways, optimistic write semantics, schema changes, external services, authorization, or weakening of tamper detection.
- **Composite endpoint:** Sprint95's `expectedCurrentVersion`, historical reducer replay and second source-version recheck remain unchanged. One read does **not** create a durable cross-request snapshot; concurrent advancement may cause 409.
- **Negative evidence:** a held uncommitted center UPDATE plus uncommitted projection row must not block a read or leak either uncommitted value. A later accepted write must still advance exact version; multiple concurrent readers with a writer must each return a complete event/audit/projection-reconcilable snapshot. Previously implemented projection-corruption, replay, CAS and center isolation tests remain mandatory.
- **Boundaries:** PostgreSQL MVCC avoids the center-row writer lock; it is **not** a guarantee against DDL/table-lock contention, resource starvation, a cross-request read transaction, or throughput/SLA performance. No real identity, clinician, record, payer, insurer, consent or Production infrastructure is provided.

## Acceptance and governance

Use existing `tests/Taranom.ClinicalCenterJournal.IntegrationTests/CenterJournalDatabaseTests.cs` so Sprint92's mandatory test census includes these genuine isolated PostgreSQL cases. Preserve the original shared **14-job** regression across both exact-head Push and mainline PR CI and independent browser/Kestrel safety tests. No new workflow job or bypass. Keep the Sprint96 PR Draft/Open/Unmerged; dependent Draft PRs #98–106 must not be merged out of order. Seventeen national blockers remain `BLOCKED_UNVERIFIED`; comprehensive national release `DENIED_BY_DEFAULT`; completion `UNBASELINED`. TRN-TECH-012 real Member BFF remains `NOT CODE-AUTHORIZED`. No Stage, QA Gate, Release or Production.
