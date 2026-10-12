# TRN-SP-32 — Secondary use privacy and consent evidence boundary

**Date:** 2026-10-10. **Base:** Sprint31 `sprint-31-care-coordination-clinical-boundary` Draft PR #38.

Business TRN-BIZ-035/036/037 → TRN-TECH-041 → Backlog TRN-EP-02 (Consent and Data Governance) / TRN-EP-06 (Personal Health Record) / TRN-EP-11 (Internal AI) / TRN-EP-13 (Member Web) → Sprint32 → code/test → complete 12-job CI → preliminary Code Review.

- **BL-320:** Introduce a pure internal synthetic-only `SyntheticSecondaryUseEvidenceGate` requiring separate member consent, exact purpose/data scope, identity/consent binding, lawful basis, effective protection, withdrawal/continuity review and auditable human governance. Every piece is an untrusted claimed test check, never operational permission.
- **BL-321:** Domain safety: scope, subject and revision mismatch fail closed; seven separate missing/rejected statuses and unknown future enum values fail; even all claimed checks `Confirmed` return **EvidencePresentOnly** (cannot activate AI training or data access). Existing consent store unchanged.
- **BL-322:** Build explicit Persian RTL `/member/consents` informational categories: care versus independent secondary use, lawful authorization, protection/withdrawal; all remain unknown, button disabled, no prechecked research consent or data sharing.
- **BL-323:** Add real xUnit matrix and three built Chromium tests. Retain all 12 existing CI gates including member/center browsers, PostgreSQL and protected clinical data **DENY-ONLY**.
- **BL-324:** Preliminary source/privacy/security review COMMENT at exact SHA and exact-head Push/PR CI evidence; independent medical/legal/privacy/security review remains outstanding.

No real consent, legally valid withdrawal effects, secondary-purpose scope approval, dataset, AI training, actual member health records, research, source access, real identity, payment, provider, Stage, QA Gate, Release or Production. No Merge or Ready.
