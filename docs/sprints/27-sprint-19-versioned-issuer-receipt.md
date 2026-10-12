# TRN-SP-19 — Pin issuer revisions to synthetic receipts

Business TRN-BIZ-043/044/053/054 → Technical TRN-TECH-028 → Backlog → Sprint19 → Code → Tests → Code Review.

- BL-190: introduce syntactically valid simulated issuer and positive snapshot version in the existing test-only source contract; reject unknown/malformed/missing values.
- BL-191: compare first and under-lock source checks for exact issuer + revision; reject changes and preserve transactional rollback with no accepted receipt/audit.
- BL-192: store source issuer and version in immutable synthetic receipt metadata and its v2 SHA-256 digest and paired audit; verify on re-read.
- BL-193: distinguish immutable **historical receipt** from claimed simulated current issuer revision through a separate fail-closed, nonauthorizing read-only inspector.
- BL-194: new PostgreSQL integration cases for ref/version mismatch, stable writes, historical issuer changes, false/revoked test source, original command mismatch and missing record; preserve all inherited 12 independent CI jobs.
- BL-195: record exact-head CI, source-level security Code Review, and limitations of double-read non-atomic external revocation; independent review still pending.

Not included: actual issuer, signed source, real immutable external authority data, regulator credential verification, clinician activation, recipient assignment, human acknowledgement/decision, real medical/financial record, real access policy, clinical network parent actor, production deployment or API/UI. Keep Draft/Open; no Ready/Merge/Stage/QA/Release/Production.
