# Sprint 106 — Real nonclinical coordination case journal and local Member Web

**Date:** 2026-10-13 | [TRN-TECH-115](../technical/TRN-TECH-115-source-backed-nonclinical-care-coordination.md) | **Business:** TRN-BIZ-046/047 | **Epics:** TRN-EP-05/12/13. Parent Sprint105 Draft/Open PR #116 `af9433f0975ef72367d75e515417a86cde7ef591`, exact Push #38396835856 and Mainline #38396904626 green, COMMENT technical review only.

**Objective:** deliver a real local operational workflow (synthetic actor claims only), using the approved nonclinical `SyntheticCareCoordinationTrail` domain reducer: source request, unverified coordinator-contact note and separately unverified clinical-source reference, with complete immutable PostgreSQL event+audit history and replayed versioned read, and a dedicated Persian RTL Member-demo view. No pretend medical referral, authentication, waiting, appointment or clinical source.

**Done criteria:** loopback Kestrel with bounded eight-field POST and non-mutating GET, genuine PostgreSQL read-only MVCC and writer serialization/CAS+SHA-256 audit, test-only SPA with manual staged source claims, exact UUID retry on uncertain ACK. Python real HTTP tests and Chromium verify positive/negative version/tenant-style scope, 3 events, concurrent writers, stale/collision and no actual service rights. Exact-head shared 14-job Push & Mainline green and COMMENT preliminary technical review, independent human review still outstanding.

**Launch:** blocked all 17 gates, first comprehensive national release DENIED_BY_DEFAULT. Keep all PRs Draft/Open/Unmerged; no Ready/Merge/Stage/QA/Release/Production.
