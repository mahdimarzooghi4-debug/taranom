# TRN-SP-46 — Synthetic urgent safety/parallel clinician referral design boundary

**Date:** 2026-10-11. **Branch:** `sprint-46-urgent-warning-parallel-referral-lineage`. **Base:** Sprint45 PR #52, stacked, all PRs remain Draft/Open/Unmerged.

**Parent chain:** Business TRN-BIZ-033/034/035/045/046 → [TRN-TECH-055](../technical/55-urgent-safety-parallel-referral-lineage.md) → Backlog TRN-EP-05/11/12/13 → Sprint46 → Code/Tests → pinned 12-job CI → preliminary COMMENT-only review.

**Product feature:** nonoperational synthetic-only typed paper lineage that distinguishes context, warning design note, proposed parallel clinical referral request, and independently claimed clinician-team receipt. No official screening/triage, real outbound warning or referral, approved emergency protocol, medicine, real member record or automated clinical decision. Per-case immutable revision and exact request UUID, scope-bounded team/evidence, replay/collision and independent stage sequencing with negative tests. Existing routine Care Coordination and complaint/service-quality bounded contexts remain unchanged. Member RTL `/member/care` gains three distinct safety-accountability descriptions and no active actions; general emergency help reminder must remain immediate and unconditional.

**Acceptance:** neither warning note nor referral request claims delivery/receipt; no source of professional qualification is invented; clinically unapproved risk classifications, staff response-time promises and regional emergency phone numbers are forbidden; no real identity/medical information/economic outcome. Four built Chromium cases and adversarial xUnit; inherited PostgreSQL and national deny-only policy green.

**Gates:** clinical validation, lawful emergency contact/provider dispatch/receipt, human accountability, privacy and records, monitored on-call capacity and infrastructure absent. No Ready/Merge/Stage/QA/Release/Production; record final exact-head CI and initial Code Review only after passing.
