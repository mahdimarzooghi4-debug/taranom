# TRN-SP-09 — Center Operator & Clinician Evidence Intake

Business TRN-BIZ-053/054/043/044 → Technical TRN-TECH-018 → Product Backlog → Sprint 09 → Code → Code Review.

- BL-090: review-only evidence classification for center manager delegation and physician qualification vs Taranom provider contract, each separately pending independent authority
- BL-091: immutable versioned synthetic intake reducer with idempotency and stale/conflict safeguards
- BL-092: case-specific center/subject ownership invariant (not real tenant authentication)
- BL-093: xUnit matrix for fake authorities, invalid/unsupported claims, old versions and absence of any APPROVED/ACTIVE clinician state
- BL-094: eleven-job exact-head CI, preserving all ten prior test suites and explicit missing-source/fail-closed rules
- BL-095: independent Code Review handoff, no real provider data/session or activation

**Excluded:** real official source/medical regulator selection, vendor/IdP credentials, actual physician identity/professional registration, human reviewer roles/threshold, approval workflow, provider activation, center panel live submission, PostgreSQL storage of real evidence, patient records, legal service contracts, financial/insurance integrations, Release/Production. No standalone network panel/parent center. PR stacked on Draft PR #15; earlier PRs remain Draft/Open/unmerged.
