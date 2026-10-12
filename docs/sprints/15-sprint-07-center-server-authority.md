# TRN-SP-07 — Center Server Authority Admission Boundary (test only)

- Business: TRN-BIZ-053/054; Technical: TRN-TECH-016; Product Backlog → Sprint 07 → Code → Code Review. Parent PR #13 remains Draft and code on a separate new Draft PR.
- BL-070: define server-owned evidence source seam; no named provider/issuer/endpoint selected.
- BL-071: explicit operation-specific checks for center manager, subject/current session, center+tenant+resource, current/revoked grant, audit.
- BL-072: browser attributes treated as untrusted hints; never allow role/tenant spoof to choose server principal.
- BL-073: **fail closed by default**; no attested provider available; even fully synthetic checks return `PreconditionsPresentOnly` and cannot call database or return PHI.
- BL-074: xUnit security-negative matrix (28 cases with parameterized variants), separate eighth CI job, seven inherited regression jobs (.NET, real PostgreSQL, two Chromium apps).
- BL-075: Code Review handoff must plainly record missing true issuer, physician/license source, center operator registry and auditable legal authorization.

Exclusions: real Identity Provider, real tenancy, doctor registration/licensing, login/cookie/CSRF policy, HTTP read/write endpoints, PostgreSQL runtime wiring, enabling center UI mutation or access to medical/finance/insurance/wallet/AI, invented production grant/timing thresholds. All PRs remain Draft/Open until independent reviews and explicit instruction; do not merge or deploy.
