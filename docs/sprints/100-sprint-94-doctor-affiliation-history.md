# Sprint 94 — A doctor's unverified center affiliation and department lineage

Date 2026-10-13. Business TRN-BIZ-053/054, Technical TRN-TECH-103. Parent Draft PR #104 at `118dabd3a7cc0a51b20fe591379406eaa95d6c5a`, exact-head full Push and PR CI 14/14 success.

Capability: historical, version-pinned synthetic physician affiliation review within *one* center, projecting original canonical PostgreSQL event/audit rows and pure domain reducer. Unverified registration, multi-department assignment, and ended affiliation all visible at exact as-of; interleaved unrelated doctor events do not pollute result or disrupt sparse pagination. No standalone provider registry, schema, real identity or access activation.

Acceptance: actual Kestrel/PostgreSQL appends, scoped source reattestation, historical end state, strict malformed/stale/hostile negatives, real Chromium manual-only UI/reload and loss of stale view, full unchanged 14-job CI Push+PR on exact SHA and preliminary COMMENT review. Keep all stacked PRs Draft/Open/Unmerged, no merge, Stage, QA Gate, Release Approval or Production; all 17 official nationwide launch gates BLOCKED_UNVERIFIED.
