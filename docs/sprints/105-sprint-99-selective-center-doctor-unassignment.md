# Sprint 99 — Remove a doctor's single department assignment

**Date:** 2026-10-13 | **Contract:** [TRN-TECH-108](../technical/TRN-TECH-108-selective-center-doctor-unassignment.md) | **Decisions:** TRN-BIZ-053/054 | **Epics:** TRN-EP-04/12.

Parent Draft/Open Sprint98 PR #109 exact `92afb4a3e16a4178e6b5002d4094433d971462a1` CI 14/14 Push #38382850571 and PR #38382857206, preliminary COMMENTED code review only. Current center supports two wards and two doctor links but only center-wide END_DOCTOR; that destructively clears both. Introduce bounded **UNASSIGN_DOCTOR** manual operation removing ONLY named ward link while preserving other wards, unverified doctor affiliation, all event history and original CAS/idempotency security. Existing PostgreSQL event+audit, HTTP, browser, versioned roster, doctor lineage, one-snapshot audit and portable manual replay reflect change.

**Tests:** .NET canonical reducer and genuine PG xUnit; Kestrel/Python and Chromium on ephemeral database, negatives, no provider privileges. Inherited 14-job exact-head Push+Mainline PR regression and COMMENT code review. No real medical/insurance/financial activity, no other user role; 17 national blockers remain unverified, launch denied, completion unbaselined. PR Draft/Open/Unmerged. No Ready/Merge/Stage/QA/Release/Production without owner approval.
