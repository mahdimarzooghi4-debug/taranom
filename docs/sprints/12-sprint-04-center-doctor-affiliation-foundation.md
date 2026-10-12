# TRN-SP-04 — Center / Department / Doctor Internal Foundation

**Phase:** Business TRN-BIZ-053/054 → Technical TRN-TECH-013 → Product Backlog → Sprint 04 → internal Code/Test/Review. Work started under owner authorization for internal code; no real provider registration or production approval.

## Selected backlog
- SP04-01: separate pure .NET `ClinicalCenterGovernance` context, **no parent network model**.
- SP04-02: center-scoped multi-department registry and unverified doctor affiliation, with department association and safe ending of affiliation.
- SP04-03: immutable accepted-event lineage, deterministic idempotency, expected-version/stale safety, fail-closed test-only center-manager and audit preconditions.
- SP04-04: negative xUnit tests for synthetic origin, cross-center collisions, revoked center authority, stale case, replay conflict, department/doctor association and ending.
- SP04-05: five-job exact-commit regression workflow: new center governance and inherited MemberGovernance, Sprint01 PostgreSQL, Sprint02 Chromium and Sprint03 proofing PostgreSQL.

**Explicit exclusions:** independent network portal/role, real center account/physician identity/licence, invitation SMS, accepted provider, approved clinical access, wallet/payments, insurance, medical patient data, real API, DB migrations, cross-center organizational hierarchy, deployment. No clinical provider or center auto-approved from synthetic events. Real center admin auth and medical qualification must be verifiable before integration.

**Gates:** new PR remains Draft/Open, inherited PR #9 and Business PR #10 unmerged, independent reviewer required. Exact-head CI success is a test gate, not a Stage, QA, or release.
