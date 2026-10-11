# TRN-SP-42 — Member-controlled clinical record disclosure boundary

**Date:** 2026-10-11. **Branch:** `sprint-42-member-clinical-record-sharing-boundary`. **Base:** Sprint41 PR #48; remains Draft/Open/Unmerged.

## Process and selected slice
Business TRN-BIZ-035, 036, 029, 041, 046, 047 → [TRN-TECH-051](../technical/51-member-clinical-record-sharing-boundary.md) → backlog TRN-EP-01/02/06/13 → Sprint42 → Code/Tests/CI → preliminary Code Review COMMENT.

A product-facing trust/scope slice, not a new journal: add an internal synthetic-only .NET precondition assessor, rich `/member/records` RTL responsibilities view, strongly negative scoped xUnit tests and four built Chromium security/nav regressions. Preserve all 12 existing CI suites with a Sprint42 workflow.

## Acceptance
1. Clinical disclosure is limited to current member-specified clinician, treatment service and exact permitted data category, with current clinical need, professional qualification, legal processing, withdrawal and audit prerequisites; all IDs are `test:` only. Missing/rejected signals deny and all Confirmed inputs **do not** permit actual access.
2. Sponsor/Employer/Network affiliate/Coordinator cannot inherit record rights. Member self-read remains separate. Clinical record sharing is never research or AI training consent. Emergency exceptions are NOT silently inferred.
3. UI includes distinct unknown states, disabled view/share/history actions, independent privacy and emergency warning, zero private connections/storage and correct accessibility navigation.
4. Inherited CI green on exact SHA, all PostgreSQL and Chromium; no Stage/QA/real patient, clinical or financial execution.

## Pending only until verified
Record final Push/PR job IDs, test counts and initial source/privacy/security Code Review **after exact SHA**. The package cannot claim real legal/medical/privacy approval, record access or national release.
