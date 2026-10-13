# Sprint 90 — Version-pinned synthetic insurance support follow-up

2026-10-13. Accepted Business TRN-BIZ-049/051/052; technical TRN-TECH-099. Parent Sprint89 PR #100 exact-head Push and Mainline PR CI 14/14 success.

Deliver a manual nonbinding follow-up against the **existing** append-only PostgreSQL support event and audit journal. A bounded test-only read preflight independently verifies current source revisions, original test-only member, case, cost evidence reference and submission UUID; it never promises atomicity across the two journals. Human command uses the existing version-locked support reducer with explicit test-only note and request UUID. A lost HTTP acknowledgement must never generate a new command UUID; only an exact replay is allowed.

Acceptance: real PostgreSQL Kestrel concurrency, stale-source/Host/forged-ref denial, Chromium manual follow-up and lost-ACK cases, unchanged fourteen GitHub regression gates and initial COMMENT review.

No insurer adjudication, real patient record, financial balance, legally submitted claim or deployment. Real member BFF not authorized. All 17 national launch gates BLOCKED_UNVERIFIED; first nationwide three-market launch DENIED_BY_DEFAULT; PR stays Draft/Open/Unmerged.
