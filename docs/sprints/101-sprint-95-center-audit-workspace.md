# Sprint 95 — Version-pinned combined clinical-center audit

**2026-10-13** | Business TRN-BIZ-053/054 and TRN-BIZ-028/030/040 | Technical TRN-TECH-104 | Based on Sprint94 Draft PR #105 exact head `0ef42d14275e5b84ff4b0f0b47933169dcae1012` with both 14/14 full regressions green.

Concrete problem: two separately correct but unpinned center history and as-of requests could be visually interpreted as one consistent audit while a competing writer advances PostgreSQL between reads. Build a source-consistent, single GET-only composite of original canonical journal event page AND as-of replayed center snapshot, bound by exact current version. Reuse only the existing canonical DB store and pure reducer. Fail closed on changed source, forged center scope, unsupported historical cursor, denial flags. Preserve existing APIs for compatibility; mark new combined view the reliable pinned read experience.

Acceptance requires genuine PostgreSQL/Kestrel tests on 7 real commands, historical before and after ending affiliation, 3+2 and 3+3+1 pages, concurrency and hostile query/host negative tests; real Chromium explicit manual click, historical and reload, stale-view clearing; existing Sprint92 Python test inclusion census and full unchanged 14-job Push and Mainline PR CI, then initial technical COMMENT review.

No doctor registration or licensing, clinical/insurer/payment authority or actual member identity. All 17 national readiness dimensions BLOCKED_UNVERIFIED, comprehensive three-market first launch DENIED_BY_DEFAULT, total completion UNBASELINED. Draft/Open/Unmerged and no Stage, QA/Release/Production.
