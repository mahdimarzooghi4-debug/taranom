# TRN-SP-165 — DRAFT / isolate synthetic late-evidence PostgreSQL read and write
Parent: Sprint 164 Draft/Open PR #178, exact base `02f6eeb962bb8bee61725aa9233b132962f55bdb`.

Technical scope: [TRN-TECH-174](../technical/TRN-TECH-174-late-evidence-postgres-role-isolation.md).
Deliver distinct PostgreSQL runtime writer and SELECT-only reader, role/ACL attestation plus actual SQLSTATE-denied statements, MVCC nonlocking read, preserved append-only/audit and a genuine isolated-PostgreSQL reader regression. No new real source, verified identity, human authorization, finance/clinical authority, external AI, provider contract, production adapter or additional API. Mandatory 14-job CI and exact-head Code Review COMMENT. Keep all PRs Draft/Open/Unmerged, no Stage, Release, Production. National launch `DENIED_BY_DEFAULT`.
