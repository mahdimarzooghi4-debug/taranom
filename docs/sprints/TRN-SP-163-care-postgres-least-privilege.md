# TRN-SP-163 — DRAFT / Care-coordination isolated PostgreSQL reader and writer

- Base Sprint 162: Draft/Open PR #176, exact SHA `376fead687fad9440d4b28c17047f9ff9a54348e`.
- Technical candidate: [TRN-TECH-172](../technical/TRN-TECH-172-care-postgres-least-privilege.md).
- Real useful engineering change: eliminate *schema-owner DB authority* from the already implemented synthetic Care Coordination HTTP path, using distinct PostgreSQL writer/reader principals plus real negative SQL checks on every test host startup.
- No speculative real trust, provider, legal/clinical/financial authority, altered event semantics, any new endpoint or Production credentials.
- Gate: full 14-job canonical CI, actual Kestrel/PG concurrency and replay tests, Code Review technical COMMENT. All PRs Draft/Open/Unmerged, no Stage, QA Gate, Release, Merge, Production; national release DENIED_BY_DEFAULT.
