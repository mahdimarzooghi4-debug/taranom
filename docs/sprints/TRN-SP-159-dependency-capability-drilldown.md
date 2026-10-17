# TRN-SP-159 — DRAFT engineering dependency drilldown
- Base: Sprint 158 `cacd4eae3f2b262fbf7e15843d7a9e4949f6d15d`, stacked Draft/Open PR #172. No merge/Stage/QA gate/release.
- Technical contract candidate: [TRN-TECH-168](../technical/TRN-TECH-168-dependency-capability-drilldown-integrity.md).
- Outcome: read-only Persian workbench traces every one of the 18 *unverified* requirement categories to the associated subset of 39 *provisional* capability entries, including Epic and evidence-lane filters.
- Implementation: UI selection from dependency row, requirement dropdown and cross-join tamper-denial invariant.
- Acceptance: existing shared 14 mandatory regression jobs plus live HTTP and Chromium new cases. No code/test reference implies Production readiness.
- Forbidden: owner assignment, external trust, acceptance, sensitive workflows, operational use, independent review impersonation, merge or deployment. National release always `DENIED_BY_DEFAULT`.
