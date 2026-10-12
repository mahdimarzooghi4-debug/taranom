# TRN-SP-50 — Unverified estimate vs post-service bill comparison

**Date:** 2026-10-11. **Stack base:** Sprint49 PR #56. **Branch:** `sprint-50-estimate-bill-source-comparison`. Keep all PRs Draft/Open/unmerged.

**Parent chain:** TRN-BIZ-034/035/049/050/051/052 → [TRN-TECH-059](../technical/59-estimate-bill-comparison.md) → TRN-EP-07/09/10/13 → Sprint50 → Code/Tests → exact-SHA 12-job CI → preliminary COMMENT Code Review.

Implement a read-only synthetic source-specific comparison across already implemented estimate + historical choice and independent post-service bill; do **not** introduce rates/formulas/issuer trust, money movements, new consent or a second ledger. The original estimate has no treatment identifier, so claimed encounter correspondence is structurally bounded but legally UNVERIFIED. Return typed differences/unknown/disputed per five lanes, never an inferred refund, overcharge, insurer coverage, payment due or official bill. Improve disconnected RTL member billing with clear three-step boundary; four real Chromium tests and adversarial xUnit. Retain real isolated PostgreSQL tests, AI no-egress, protected record denials and national first-release blocker.

**Gates:** no actual services, identity, lawful source integration or external expert admission. No Merge, Ready for Review, Stage, QA Gate, Release Approval, Production. National launch DENIED_BY_DEFAULT.
