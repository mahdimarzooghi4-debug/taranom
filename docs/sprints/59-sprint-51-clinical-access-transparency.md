# TRN-SP-51 — Member-visible access request and oversight paper trail

**Date:** 2026-10-11. **Branch:** `sprint-51-clinical-access-request-transparency`. **Stack:** Sprint50 PR #57 Draft/Open.

**Parent:** TRN-BIZ-029/035/036/041/046/047 → [TRN-TECH-060](../technical/60-clinical-access-request-transparency.md) → backlog TRN-EP-01/02/06/10/13 → Sprint51 → code/tests → exact-head pinned 12-job CI → COMMENT-only preliminary review.

Build new synthetic-only internal immutable, scope-pinned **paper-note** trace for claimed clinician need request, member concern and unverified oversight note, composing existing care disclosure gate. Add independent *claimed member read* projection via existing `MemberSelfReadBoundary` that denies sponsors and cross-member/tenant even paper metadata; never actual access or clinical decision. Reject rebind, replay conflicts, no root, duplicates and stale revisions. Enhance existing RTL Member records route, add four built Chromium tests, adversarial xUnit, maintain all inherited real PostgreSQL, policy and Chromium CI. **No medical data, consent, identity provider, actual patient activity log, case resolution, automatic decision or provider credential.**

Open gates are real patient/clinician authority, lawful disclosure, actual append-only audit, authentication, retention/privacy and clinical/legal expert evidence, national infrastructure. Comprehensive first nationwide release stays DENIED_BY_DEFAULT. No Ready/Merge/Stage/QA/Release/Production.
