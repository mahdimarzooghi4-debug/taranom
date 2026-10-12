# TRN-SP-14 — Reviewer provenance and independence preconditions

Process: Business TRN-BIZ-043/044/053/054 → Technical TRN-TECH-023 → Backlog → Sprint 14 → Code/Tests → Code Review.

- BL-140: bind the stored immutable human note to the exact submitted accepted evidence event and a source-derived reviewer identity candidate, keeping every `test:` input untrusted.
- BL-141: deny self-review when note reviewer equals the submitting actor or the reviewed physician/representative; source origin and reviewed evidence reference must match.
- BL-142: no legitimate reviewer service selected; missing source, missing identity mapping, stale/revoked delegation, absent audit or unknown source provenance fail closed.
- BL-143: even all-positive test double reports only `PreconditionsPresentOnly`, not authorized/approved clinician; no new review decision state or permission.
- BL-144: run 29 new real PostgreSQL tests, preserve 77 prior evidence/review tests and twelve existing end-to-end CI jobs including both Chromium UI suites.
- BL-145: record source-level review and explicitly block any production interpretation or connection to center panel.

**Not included:** real reviewer role/identity, authoritative eligibility/qualification source, actual COI criteria, provider contract, medical decision, appeal, patient data/consent, operational reviewer API/portal, user login, RLS, production persistence, external audits, payments or clinical activation. Independent Code Review and business contracts required. No Ready/Merge/Stage/formal QA/Release/Production.
