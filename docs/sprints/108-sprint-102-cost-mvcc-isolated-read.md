# Sprint 102 — Real PG MVCC for cost-awareness history

**Date:** 2026-10-13 | **Technical contract:** [TRN-TECH-111](../technical/TRN-TECH-111-cost-mvcc-read-consistency.md) | **Business:** TRN-BIZ-050/051/052 | **Epics:** TRN-EP-09/12/13.

**Parent:** Sprint101 PR #112 Draft/Open HEAD `d8c8b693c8451f039ae492403a326ca2f18d86c8`, exact Push #38388873533 and Mainline PR #38388880363 both SUCCESS 14/14, preliminary Code Review COMMENT, not an independent human approval.

**Real defect:** the existing synthetic cost preview and accepted-choice GET uses the writer's FOR UPDATE row lock inside default transaction. Fix by one read-only repeatable-read PostgreSQL snapshot over preview header, five source lines and every choice+paired audit, leaving the mutator's original FOR UPDATE/CAS/idempotency untouched. No new endpoint, schema, monetary entitlement, medical permission or source authority.

**Acceptance:** extend the *existing* actual Npgsql/Kestrel full-CI concurrency probe. Register genuine 5-line synthetic preview, physically lock its database row using an uncommitted legal OLD+1 update, confirm GET completes and sees committed choiceRevision=0 with all rights false, roll back then run eight successive genuine HTTP choices mixed with concurrent readers. Audit count precisely eight; no mixed source/revision, no stale grants, final Deferred status. Preserve entire 14-job Push+Mainline full CI, Chrome, Postgres, security and recovery checks; COMMENT technical review only after final HEAD green.

National release still DENIED_BY_DEFAULT, all 17 national gates BLOCKED_UNVERIFIED and completion UNBASELINED. Keep Draft/Open/unmerged, no Ready/merge/Stage/QA/Release/Production.
