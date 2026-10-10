# TRN-SP-06 — Transactional Center/Doctor Storage Foundation

Business TRN-BIZ-053/054 → Technical TRN-TECH-015 → Product Backlog → Sprint 06 → Code → Code Review. No real admin identity/center contract exists yet.

Scope:
- BL-060: isolated PostgreSQL relational projection for one center and its multiple departments, doctors and department assignments;
- BL-061: row-locked expected-version concurrency, immutable accepted event and audit with transactional rollback;
- BL-062: distinct append-only denied-attempt audit with no provisional center rows after a rejection;
- BL-063: read-time event rehydration and relational projection-divergence checks;
- BL-064: 18 genuine isolated PostgreSQL negative and race-condition integration tests;
- BL-065: seven independent CI jobs including inherited .NET/PG/Chromium regression.

**Forbidden:** real center account/session/tenancy, actual physician identity, medical qualification or automatic medical access, real API, frontend connection, insurer, wallet, payments, fake provider credentials/coverage, network parent portal, migrations for production, release or deployment.

The center clinical web remains explicitly disconnected; no dummy adapter is injected into the browser. Business PR #10 and stacked technical PRs stay Draft/Open and unmerged. After green CI, independent Code Review and verified real center manager/physician identity contracts are still required.
