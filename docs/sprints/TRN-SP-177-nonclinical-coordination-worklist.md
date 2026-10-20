# TRN-SP-177 — DRAFT / operational nonclinical coordination worklist

Base: Draft/Open PR #190, HEAD `457cf5ca0968b570927f24c773964bd6ee500bee`; unchanged protected `main`.

Approved boundary: TRN-BIZ-046, existing TRN-TECH-115, care PostgreSQL audit source and explicit center nonclinical test workflow. New contract: [TRN-TECH-186](../technical/TRN-TECH-186-nonclinical-coordination-worklist.md).

Implement a functional discovery step in the Center QA workflow: page through source-verified synthetic care requests, inspect claim counts without revealing notes/member identities, explicitly select a `test:` case, independently open it in the same existing care journal and only then choose a manual unverified coordinator-contact action. Backend: CI-only read-only GET and SELECT-only PostgreSQL bounded keyset, verified-by-audit each candidate including lookahead; no cross-org join or real health-data access. QA: three Playwright Chromium/PostgreSQL scenarios and full 14-job canonical CI on exact commit; technical Code Review COMMENT. All previous stacked PRs Draft/Open/Unmerged; no merge, Stage/QA Gate/Release Approval/Production, release `DENIED_BY_DEFAULT`.
