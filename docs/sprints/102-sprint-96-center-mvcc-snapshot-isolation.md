# Sprint 96 — Source-consistent, non-writer-locking center reads

**Date:** 2026-10-13 | **Technical:** [TRN-TECH-105](../technical/TRN-TECH-105-center-mvcc-snapshot-isolation.md) | **Business:** TRN-BIZ-053/054; TRN-BIZ-028/030/040 | **Epics:** TRN-EP-04/12.

Parent Sprint95 PR #106 Draft/Open HEAD `6ce631da74aa77b9caec3e971133f48d555ddbd7` has exact-head Push #38369180585 and Mainline PR #38369189817 each **14/14 SUCCESS** and an existing preliminary `COMMENTED` technical review. No merge or Stage authorized.

**Single systemic defect to close:** all center GET history, roster, audit and snapshot handlers reuse `PgClinicalCenterJournal.ReadAsync`. Its existing row `FOR UPDATE` lock needlessly serializes readers behind active writer transactions. Naively making a READ COMMITTED unlocked read would permit mixed journal/projection revisions. Replace the read with a verified immutable PostgreSQL MVCC snapshot while preserving the unchanged serialized write path.

**Acceptance:** (1) source event/version/audit/projection attestation in one `REPEATABLE READ READ ONLY` transaction with unlocked center SELECT; (2) unchanged writer lock and revision CAS; (3) an actual uncommitted UPDATE and inserted projection cannot block, leak or corrupt a read; (4) concurrent readers/writers get only coherent source versions, with existing idempotency, rollback, tamper and cross-center tests intact; (5) no extra UI or APIs; (6) all existing 14 shared CI jobs and exact-head Push/Mainline PR green; (7) preliminary COMMENT Code Review after CI.

**Scope:** synthetic `test:` data and ephemeral PostgreSQL only. No new manager login, doctor licence, real provider, insurer, payment, member clinical rights, treatment records or Product release authority. National release remains **DENIED_BY_DEFAULT** with all 17 external gates **BLOCKED_UNVERIFIED**, total completion **UNBASELINED**, and real member BFF TRN-TECH-012 **NOT CODE-AUTHORIZED**. All PRs Draft/Open/Unmerged until explicit owner instruction.
