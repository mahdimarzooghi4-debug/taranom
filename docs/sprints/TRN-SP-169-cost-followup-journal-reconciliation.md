# TRN-SP-169 — DRAFT / insurance-follow-up source-confirmed recovery
- Stacked on Draft/Open/Unmerged Sprint 168 PR #182, exact head `ca64f77f7434c8a98bce4c40213afb8a8dcdc4c0`.
- Source contract: [TRN-TECH-178](../technical/TRN-TECH-178-cost-followup-journal-reconciliation.md).
- Functional deliverable: member insurance-dispute note is now confirmed by **exact original UUID, note, actor, version and PostgreSQL source** rather than an aggregate revision or success HTTP code. The new explicit GET-only action can reconcile an accepted-but-lost follow-up without risking duplicate POST; manual same-UUID retry stays available when a GET cannot prove the event.
- Limited C# response addition: only the already persisted test-only requestId per follow-up. No schema changes, new endpoints, real insurer, KYC, claims, money or clinical rights.
- Gate: three new Chromium/PostgreSQL adversarial cases; canonical full 14-job CI; technical Code Review COMMENT on exact green SHA. All prior PRs and this one Draft/Open/Unmerged. No Ready for Review, Stage, QA Gate, Release, Merge or Production; national release DENIED_BY_DEFAULT.
