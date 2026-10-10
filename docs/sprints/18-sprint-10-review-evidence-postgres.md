# TRN-SP-10 — Transactional Evidence Review Work Item Journal

Business TRN-BIZ-053/054/043/044 → Technical TRN-TECH-019 → Product Backlog → Sprint 10 → Code → Code Review.

- BL-100: genuine test-only relational PostgreSQL persistence scoped to one center, one subject and one subject category
- BL-101: immutable accepted evidence event, pending review work item and accepted audit in one transaction; rollback if any write fails
- BL-102: replay entire accepted history, attestation digest and work-item/audit consistency; fail closed on inconsistency
- BL-103: row-lock per case, idempotent historical replay, conflicting replay, expected-revision/CAS and rejection audit
- BL-104: negative genuine PostgreSQL integration suite with concurrency, tampering, no real IDs, no subject mixing, invalid claim and failure injection
- BL-105: exact-head 12-job CI, preserving previous 11 jobs and their Chromium/postgres regressions; independent Code Review outstanding

Explicit exclusions: verified license, review approval/denial decisions by live reviewer, real center-user IdP, production credential, real evidence files/national IDs, center panel write/read/API, service provider activation, medical access or wallet/payment. This is ONLY synthetic test code. No separate network manager role/portal. Keep stacked PR Draft/Open and do not merge, mark Ready, Stage, formal QA, Release or Production.
