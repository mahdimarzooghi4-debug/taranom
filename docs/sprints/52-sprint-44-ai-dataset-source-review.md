# TRN-SP-44 — Internal AI data-source eligibility review (synthetic only)

**Date:** 2026-10-11. **Stack:** Sprint43 PR #50. **Branch:** `sprint-44-ai-dataset-consent-partition-boundary`. Keep Draft/Open/Unmerged.

## Parent sequence
Business TRN-BIZ-033/035/036/037/038/039 → Technical [TRN-TECH-053](../technical/53-synthetic-ai-dataset-consent-partitions.md) → Backlog TRN-EP-02/11/13 → Sprint44 → code/tests/12-job CI → exact SHA review COMMENT.

## Scope
- A **pure .NET synthetic-only** inspection of frozen *claimed* training/evaluation source references. Reuse existing independent SecondaryUseEvidenceGate, preserve existing AI candidate/release trail untouched. No new service, dataset, trainer, inference endpoint, stateful model registry, or fake Production application.
- Enforce exact tenant/member-independent research-purpose/data scope, consent revision, legal processing basis, explicit withdrawal/restriction review, source rights/quality, partition isolation, and training-policy explicitness. Unknown/rejected deny. Counts and SHA fingerprint are only claimed input diagnostics, no data-body or authorization.
- Strengthen `/member/consents` in Persian RTL with explicit source-to-governed-candidate explanation; all stage evidence unknown and disabled. Four Chromium cases prove no inferred opt-in, no external AI or private API, unchanged navigation focus and browser storage.
- Extend adverse xUnit tests for partitions, no cross-scope reuse, every separate consent/source check, source duplicate across different revisions, no numeric automatic model/data policy. Preserve 12 independent jobs, genuine isolated PostgreSQL and no-egress/AI policy/national blockers.

## Gate
Actual training policy and cadence, real data consent/legal basis, dataset definitions, retention, evaluation, model family, independent medical/technical committee and final accountable human are **OPEN**. Release remains DENIED_BY_DEFAULT. No Merge/Ready/Stage/QA/Release/Production. Document exact CI & preliminary Code Review only when verified on the final head.
