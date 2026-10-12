# Sprint 87 — Mainline full regression protection

Date: 2026-10-12. Contract: TRN-TECH-096. Parent SHA: `0d8cad694292c7810c9f292598fcbb14a450f695`. GitHub's completed Integration PR #97 preserved all Sprint 01–86 and parallel PR #10/#62/#89/#93 code ancestry. Its Push/PR CI passed all 14 jobs on the tested tree; the merged `main` had no Actions run.

Deliverables: mandatory exact-event `main` full-14-job same-commit read-only dispatcher; static missing-file admission denial; positive and negative security tests; separate Sprint87 exact-branch/full-regression dispatcher; no duplicated or downgraded jobs. Execute exact-SHA Push and PR CI and technical COMMENT Code Review.

**Risk and acceptance:** A green Sprint87 PR means code is eligible for owner-reviewed Merge, **not** that post-merge main has already run. After a future explicit Merge, inspect actual main head and check real full-CI run for that head. Full nationwide product release remains DENIED_BY_DEFAULT with all 17 blockers; no Stage/QA Gate or Production.

The Sprint87 branch-only dispatcher uses historical PR base `sprint-86-postgres-cost-choice-workflow` intentionally so it will not duplicate the mandatory mainline PR workflow on all future PRs. For this PR against `main`, exact-SHA Push CI comes from the Sprint87 dispatcher and PR CI comes from the new mainline dispatcher. This removes the obsolete integration-to-main dispatcher and avoids 28 redundant jobs per future PR.
