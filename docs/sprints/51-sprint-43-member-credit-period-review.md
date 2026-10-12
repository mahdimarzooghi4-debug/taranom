# TRN-SP-43 — Member-credit historical period review and lifecycle visibility

**Date:** 2026-10-11; **Branch:** `sprint-43-member-credit-period-review`; **Base:** Sprint42 PR #49. Draft/Open/Unmerged.

**Business → Technical → Backlog → Sprint → Code → Tests → CI → Code Review**

- Approved Business: TRN-BIZ-012 retention on nonrenewal, -013 reactivation resumes new accrual only from reactivation, -014 late recorded old-period claims require verification/review, -015/016 initial 90 complete days from official activation, -003/005 single personal noncashable wallet, -024 subscription payment requirement.
- Technical: [TRN-TECH-052](../technical/52-synthetic-member-credit-period-review.md).
- Backlog: TRN-EP-03/08/13.
- Code: **compose rather than duplicate** existing membership waiting and historical window reducers; batch-project unverified claims with strict source/period validation and canonical fingerprint. No journal/ledger, payout, new waiting policy, payment activation or auto-attribution.
- Member Web: two product screens explain separate membership and credit lifecycle, all statuses unknown and actions disabled.
- Tests: adversarial xUnit + four real Chromium, full inherited 12 CI suites, preliminary COMMENT review anchored to exact CI-green SHA.

**Acceptance:** no credit from gap, no inferred active/open coverage, no cross-period implicit apportionment, reactivation does not restart original wait, retrospective historical evidence is never a transfer instruction, historical replay/source lineage cannot be silently duplicated, mixed claims do not become a balance. No real sources/amounts. National official release denied by default.

**Evidence pending:** exact final SHA, Push/PR runs, test counts, commentary Code Review recorded only after green.
