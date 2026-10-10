# TRN-SP-31 — Care pathway coordination and clinical-authority boundary

**Date:** 2026-10-10. Base `sprint-30-support-complaint-insurance-boundary` (Sprint30 PR #37 Draft/Open/Unmerged). Business TRN-BIZ-034/035/046/047 → Technical TRN-TECH-040 → Backlog (care coordination + Member Web) → Sprint31 → .NET contract → tests → Member UI → actual built Chromium → CI → preliminary review.

- **BL-310** — Create an internal pure synthetic-only nonclinical coordination trail with three typed actions: request, unverified coordinator contact and unverified reference to clinical-source document. Require `test:` names, nonempty request IDs, optimistic revision, immutable scope and original request binding. No genuine provider data, medical decision, treatment/referral action, consent or money.
- **BL-311** — Safe replay/idempotency and explicit separation of counts of unverified coordinator notes vs clinical-source references; no verified or active clinical outcome, scheduling, appointment or automatic referral.
- **BL-312** — Add visible Persian RTL responsibility map on `/member/care` with three unavailable phases and human clinical decision boundary, independent emergency-help notice, disabled CTA, no real clinical data/API/session.
- **BL-313** — Add comprehensive xUnit negative/security tests and four built-Chromium cases; preserve 12 inherited CI jobs, CSP `connect-src 'none'`, protected clinical-data DENY-ONLY and all real PostgreSQL regressions.
- **BL-314** — Source-level security/product Code Review COMMENT on final SHA, exact Push/PR CI evidence, no claim of independent medical/legal/financial/security review.

No operational clinical capability, provider/insurer integration, professional licence, medical record, research AI consent, emergency triage rules, provider dispatch, wallet flow, Stage/Release/Production admission, Ready or Merge.
