# PR #290 — Consolidated integration verification ledger (2026-11-08)

**Status: DRAFT / NOT RELEASE APPROVED.** This is a technical verification ledger, not an approval, completed independent review, or readiness certificate.

## Exact candidate and integration ancestry

- Repository: `mahdimarzooghi4-debug/taranom`.
- Integration branch: `feat/figma-backend-integration`.
- Verified pre-ledger source head: `9e4aaf55dfc39497c54bfc92cc8f1feaa08e0e58`.
- Main baseline before this ledger: `0c5c079c64ae7374e65044896a40361bfa705332`.
- PR #290 was retargeted from `feat/real-backend-identity-appointments` to `main` solely to run the canonical full-regression PR gate on the *combined* candidate. It remains Draft/Open/Unmerged.
- PR #288 (`b6b1f24b3babb027483df93e1fb6153338f97b5e`) is an ancestor of the PR #290 source head: comparison `#288...#290` was 83 commits ahead, zero behind. No parallel cherry-pick or independent merge of #288 is necessary for this candidate.
- PR #289 (`2cdb29d3f4c7298702c5f024c37c3e6fc09c793a`) is an ancestor of the PR #290 source head: comparison `#289...#290` was 58 commits ahead, zero behind. PR #289 remains its own Draft/Open/Unmerged request.
- Comparing `main...#290` before this ledger returned 87 commits ahead, zero behind. This is *code ancestry*, not proof of full integrated test or deployment readiness.

## Verified pre-ledger CI evidence

1. PR #289 source head `2cdb29d3f4c7298702c5f024c37c3e6fc09c793a`: canonical `Taranom Mainline Full Regression` run **42257896301**, all **14/14** jobs successful. That run is valid evidence for PR #289's head **only**, not PR #290's current combined head.
2. PR #290 source head `9e4aaf55dfc39497c54bfc92cc8f1feaa08e0e58`: `Figma Appointment Integration` run **42392829577**, **2/2** jobs successful (`browser-integration`, `backend-postgres-contracts`). These jobs cover connected frontend/API slices but do **not** replace the canonical 14-job regression.
3. PR #288 source head `b6b1f24b3babb027483df93e1fb6153338f97b5e`: `Taranom Mainline Full Regression` run **42029331421** failed four jobs. The logs show rejected disconnected-bundle verification of the changed member/center preview and loopback CSP checks in operations/end-to-end flows. This is historical red evidence, **not** an assertion that identical failures persist in the later combined code.
4. The canonical `.github/workflows/taranom-mainline-full-regression.yml` is configured for PRs targeting `main` and calls the pinned 14-job reusable workflow `taranom-full-regression-reusable.yml`. Retargeting a PR alone did not yield a new run at the time of the ledger; this committed update is intended to cause the `synchronize` event for the combined candidate.

## Mandatory verification after this commit

- [ ] Refresh the exact resulting integration branch HEAD and compare it to the queued CI run's source SHA.
- [ ] Require **all 14** canonical regression jobs green for that exact new HEAD, including internal policy tests, PostgreSQL, real Chromium, center interlocks, review evidence and member/center/operations journeys.
- [ ] Require the two targeted Figma backend/browser jobs green for the same resulting HEAD or explain a skipped path-filter trigger with an independent fresh run.
- [ ] If any CI is red, investigate that failure only; do not add product features until green.
- [ ] Perform an actual technical Code Review on the integrated change set and record findings; `COMMENTED` is not independent human APPROVE.
- [ ] Reconcile PR #288/#289/#290 disposition only after integrated checks and explicit owner instruction on merging or closing PRs.

## Product, authority and release limits

The integration has not connected a live identity provider or approved first Admin bootstrap; no verified doctor/provider registry, publication authorization, clinical record, consent authority, paid membership/activation, insurer, wallet, settlement, or real finance operation is claimed. Browser-hosted bearer session and same-origin API routing remain deployment prerequisites. Android device validation remains pending.

The national launch checklist remains `DENIED_BY_DEFAULT` with all 17 domains `BLOCKED_UNVERIFIED`. No Stage, formal QA Gate, Release Approval, Production deployment, or national launch is authorized by this ledger or by synthetic/ephemeral CI. No change to these governance conditions is introduced.
