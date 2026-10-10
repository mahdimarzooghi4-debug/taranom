# TRN-SP-08 — Data-access admission interlock (closed by default)

Process: Business TRN-BIZ-053/054 → Technical TRN-TECH-017 → Product Backlog → Sprint 08 → Code/Test/Code Review.

- BL-080 isolate private data access behind a test-only application-boundary interface; no HTTP/session or actual protected-data port implementation.
- BL-081 do NOT treat even a fully-positive simulated authority result as an actual grant; all reads and writes remain disconnected/denied.
- BL-082 cover seven exact read/write operation types with fake browser role/tenant, conflicting center, revoked permission, missing audit, stale/unknown identity and absent provider conditions.
- BL-083 seed and verify genuine synthetic PostgreSQL state persists **unchanged** after denied or untrusted requests.
- BL-084 add two independent CI jobs and retain the eight predecessor jobs, fix any failure before expanding scope.
- BL-085 provide Code Review handoff highlighting test-source provenance and direct journal friend-test bypass risk.

Excluded: real identity/authentication, production tenancy, API/BFF, enabled center UI writes, certified physician, actual private medical/finance reads, activated provider, fund/insurance, production migration and deployment. Keep all PRs Draft/Open, no Merge/Stage/QA/Release without explicit approval.
