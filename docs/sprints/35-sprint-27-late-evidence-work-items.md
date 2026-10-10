# TRN-SP-27 — Unverified late-benefit evidence and claimed human-note journal

**Date:** 2026-10-10. **Base:** `sprint-26-member-coverage-window-observation` (Draft PR #33).

Business TRN-BIZ-012/013/014/015/016 → Technical TRN-TECH-036 → Backlog → Sprint27 → domain contract → isolated PostgreSQL code → unit/integration tests → exact-head CI → preliminary Code Review.

- **BL-270:** internal synthetic evidence work item capturing case, member, exact attribution interval, opaque document reference and unverified submitter marker. No income amount, wallet mutation or authorization outcome.
- **BL-271:** separate claimed human-note action with immutable link to exact original submission/evidence/member/interval and distinct claimed reviewer marker. The note is neither a trusted professional review nor an APPROVED/REJECTED decision.
- **BL-272:** idempotent and versioned pure .NET reducer. Deny stale writes, changed request replay, cross-member/case/period or evidence substitution, notes without submission and duplicate new submission.
- **BL-273:** durable synthetic-only PostgreSQL journal with row-locked writes, atomic event+paired audit+CAS, history and digest verification before replay, failure-injected audit rollback and least-privilege CI user. Explicitly NOT a production financial ledger or legal signature.
- **BL-274:** standalone xUnit and real PostgreSQL adversarial tests plus inherited **12 independent CI jobs**, including both Chromium shells and protected-data DENY-ONLY. Preliminary source-level Code Review is COMMENT; independent financial/legal/clinical/security review not performed.

Unapproved: real member/issuer, professional appointment, official reviewer conflict criteria, income/custodian/credit formula, legal evidence use, bank/insurance/payment integrations, actual clinical care and anything Production. No Ready/Merge/Stage/QA/Release/Production. Every item `test:` synthetic, no patient records or raw provider text.
