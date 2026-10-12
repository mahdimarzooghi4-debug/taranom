# TRN-SP-05 — Clinical Center Web UI Foundation

Business → Technical TRN-TECH-014 → Product Backlog → Sprint 05 → Code/Test/Code Review (Draft).

## Backlog and implementation scope
- SP05-01: standalone Persian RTL center-management UI in `apps/center-web` with **one panel / five routes** (not a distinct network management portal).
- SP05-02: visually distinct department, affiliated-doctor, qualification and audit sections, with no invented real center data.
- SP05-03: accessible navigation and mobile menu, browser-history focus, no-storage/no-remote-call safe locked workflow dialog; action buttons for Define Department, Link Doctor, Associate to Department and End Affiliation show only prerequisites and cannot commit.
- SP05-04: negative Chromium E2E cases for deep links, spoofed manager/tenant, absence of real doctor/PHI/wallet data, inaccessible write actions, privacy/credential protection, unknown routes, modal keyboard and responsive routes.
- SP05-05: exact-head six-job CI with new browser suite and regressions from Sprints 01–04.

No real center session, manager authority, source-backed department/doctor read model, clinical authentication/qualification, patient record access, API, database mutation or production integration. The pure .NET state machine from Sprint 04 is **not** called from React. Its caller-provided flags are untrusted and cannot become server authorization.

PR stacked on Sprint 04 Draft PR #11, business reference Draft PR #10. No Merge, Stage, QA Gate, Release or Production without explicit approval and independent security/clinical review.
