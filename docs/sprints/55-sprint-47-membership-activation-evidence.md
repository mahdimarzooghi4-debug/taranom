# TRN-SP-47 — Membership activation evidence checklist and correction-aware provenance

**Date:** 2026-10-11. **Branch:** `sprint-47-membership-activation-evidence-preconditions`; **Base:** Sprint46 PR #53. **State:** Draft/Open/Unmerged.

**Parent:** TRN-BIZ-015/016/017/018/019/020/021/022/023/024/028/029 → [TRN-TECH-056](../technical/56-membership-activation-evidence-preconditions.md) → backlog TRN-EP-01/03/08/13 → Sprint47 → code/tests/CI/COMMENT-only review.

**Package:** one new INTERNAL pure `SyntheticMembershipActivationEvidenceGate` that COMPOSES existing `ProofingCaseJournal` and `SyntheticSubscriptionPayerBoundary`, structurally replays all identity corrections and blocks stale proofing, distinguishes pending/unavailable/mismatch, pins exact beneficiary/tenant and wallet ownership, requires separate claimed licensed identity-source, lawful basis, terms and all-other-conditions evidence. No legal automatic membership activation conditions invented. Even positive caller claims are not trusted and NEVER create official activation or start 90-day clock. Existing domain reducers unchanged.

Extend `/member/membership` with four independent evidence/authority stages and disabled controls. Adversarial xUnit: source/gate failures, correction replay, stale revision, sponsor scope/privilege, arbitrary identity enum, unknown evidence, no monetary or personal record. Four Chromium route/navigation/CSP checks. Inherit full 12-job CI including genuine isolated PostgreSQL and deny-only national launch/external AI gates. Record final SHA exact CI, preliminary COMMENT-only review and blockers when green.

**No real member/payment/provider sources.** Official nationwide three-market first launch remains DENIED_BY_DEFAULT. No Ready/Merge/Stage/QA/Release/Production.
