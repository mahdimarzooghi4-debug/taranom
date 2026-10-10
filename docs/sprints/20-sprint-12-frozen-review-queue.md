# TRN-SP-12 — Consistent frozen membership for synthetic review queues

Business TRN-BIZ-053/054/043/044 → Technical TRN-TECH-021 → Product Backlog → Sprint 12 → Code → Code Review.

- BL-120 Preserve the existing **live** queue read behavior and expose a separately named frozen as-of capture path for one synthetic center and subject category.
- BL-121 Capture all accepted unverified submissions through one PostgreSQL REPEATABLE READ transaction and atomically store their exact membership/ordering manifest and consistency digest.
- BL-122 Page over immutable captured records; new commits before/after alphabetic cursors must not enter captured pages; a newly requested capture should include those commits.
- BL-123 Limit snapshot data-source SQL role to accepted-evidence SELECT plus test snapshot-table SELECT/INSERT; reject accepted history mutation, invalid or mismatched cursor and scope.
- BL-124 Add 20 genuine PostgreSQL test cases including race-prone insert ordering, corrupt event, cross-center, category separation, test-only immutable history, empty snapshot and version changes.
- BL-125 Keep all **12 independent CI regression jobs** and 39 existing review PostgreSQL scenarios, fix red exact-head CI until green, record source-level Code Review and unresolved identity/legal/clinical contracts.

Unresolved: any real reviewer/center manager source; legitimate role grants; official physician credentials and valid service-contract signatures; retention/expiry; real file storage; live API/UI; deployment, audit export, RLS, backup or operational scale. Do not invent a retention duration, compliance claim or model of automatic human decisions. All PRs remain Draft/Open, no Ready/Merge/Stage/QA/Release/Production without explicit human approval.
