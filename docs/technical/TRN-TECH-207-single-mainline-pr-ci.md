# TRN-TECH-207 — Canonical single mainline PR CI routing

## Issue reproduced
PR #211 triggered 85 complete 14-job reusable workflow invocations. The canonical `taranom-mainline-full-regression.yml` owns the required main push/main PR check, but 84 historical Sprint dispatchers also subscribed to `pull_request.branches=[main]`. All 85 were queued, not proven failed.

## Engineering change
Only those **84 historical Sprint dispatchers** remove their main-PR subscription. Their original pinned `push.branches` and `workflow_dispatch` entrypoints and unconditional call to `taranom-full-regression-reusable.yml` remain intact. All other historic stacked-branch target patterns remain unchanged.

The canonical `taranom-mainline-full-regression.yml` remains the **sole** main-PR full regression route. The reusable 14 jobs, commands, tests, Python AI discovery, Chromium, PostgreSQL, policy security checks, security-scoped read-only permissions, and no deployment approvals are unchanged.

`check_public_ci_workflow_trust.py` treats historical dispatchers previously targeting main as `push + workflow_dispatch` only, and fails closed if any restores a main PR trigger or weakens the pinned job call. The mainline dispatcher is still required to trigger on both main push and main PR. Negative mutation regression in `test_mainline_full_ci.py` exercises restoring an unauthorized legacy PR trigger.

## Scope and limits
This is CI fanout repair, **not** deactivation of tests, passing QA, Stage or Release. Branch push or manual runs for historical purposes can still invoke the complete regression intentionally. A PR to main will launch one canonical regression workflow once the change is merged to main; historical queued runs created before the configuration change cannot be retroactively removed. Do not merge/deploy without owner approval. PR remains Draft/Open pending exact-head CI and technical review.
