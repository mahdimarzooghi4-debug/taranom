# TRN-SP-03 — Proofing review core and stacked regression assurance

**Recorded:** 2026-10-10. This is a substantive code extension to Draft PR #9 under the owner's broad code authorization, not a real IdP or KYC integration.

## Implemented Business-governed logic (TRN-BIZ-018…024)

- Identity proofing from a **licensed specialist contracted to Taranom** is required before automatic membership activation (TRN-BIZ-018/019); the actual provider and Web authentication operator are not selected.
- A future .NET owner must independently establish **source validity, applicant binding, recency and whether official activation was already recorded**. None is asserted by this library. The pure `IdentityProofingReview.Assess` output is not a provider API code, persisted state, identity assertion or permission.
- Provider outage/inconclusive outcome -> **await authoritative verification**, with **no invented retry interval/limit** (TRN-BIZ-020).
- Authoritatively verified definitive mismatch **before activation** -> **applicant may correct and must reverify** (TRN-BIZ-021/022/023). Changing identity data invalidates any previous proof; no national ID or PHI is stored in this code.
- Active member identity-change/mismatch -> **separate governance**, never applicant self-service by default.
- A validated positive observation -> **only proofing evidence**. It never starts the official 90-day clock, activates membership, validates a subscription payment, authorizes a read or issues medical credit. Further conditions remain separate and undecided (TRN-BIZ-016/017/024).

## Exact-head regression requirements

For this stacked code PR, `.github/workflows/sprint-03-member-governance.yml` must run **three independent jobs** on the exact current SHA:
1. The new standalone MemberGovernance xUnit suite (including proofing review + temporal/access prerequisites and forbidden external adapters).
2. Existing Sprint 01 .NET synthetic xUnit plus real PostgreSQL smoke/failure-injection.
3. Existing Sprint 02 React/TS typecheck + production Vite build + real Chromium Playwright checks against built bundle.

This does not imply a real member integration test or independent Code Review. Open blockers remain [Issue #6](https://github.com/mahdimarzooghi4-debug/taranom/issues/6) and [Issue #8](https://github.com/mahdimarzooghi4-debug/taranom/issues/8). All stacked PRs stay Draft/Open until independent gates genuinely pass; no Stage/QA/Production claim.
