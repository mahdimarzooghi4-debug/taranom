# TRN-SP-29 — Append-only synthetic pre-treatment awareness journal

**Date:** 2026-10-10. **Base:** Sprint28 `sprint-28-nonbinding-cost-awareness-foundation`, PR #35 Draft/Open.

Business TRN-BIZ-050/051/052 → Technical TRN-TECH-038 → Sprint29 → isolated .NET PostgreSQL journal → adversarial PostgreSQL tests → exact-head 12-job CI → preliminary source review.

- BL-290 — Register each synthetic five-component `SyntheticCostPreview` as an immutable preview-version snapshot. Validate its canonical fingerprint, status, member and input; same snapshot is idempotent, changed snapshot under same UUID is conflict.
- BL-291 — Durably capture only `CostAwarenessRecorder`'s three **nonbinding** synthetic choices, pinned to exact immutable `test:` member, preview ID/version and fingerprint; enforce full history verification before replays, request idempotency, scope/version checks, and head CAS.
- BL-292 — Separate CI-only PostgreSQL role, append-only event and paired audit, atomic commit/rollback and concurrency safety; fail closed for mismatched audit, digest/JSON corruption and privilege violations. No authentic identity, legal signature or DBA-resistant evidence claim.
- BL-293 — 14 adversarial PostgreSQL integration cases plus all inherited .NET, actual browser and 12 CI job gates. Preserve `Proofing isolated PostgreSQL schema` xUnit collection so concurrent role DDL doesn't reproduce Sprint27's `XX000` defect.
- BL-294 — Code Review COMMENT against exact HEAD and final Push + PR CI evidence; independent financial, legal, clinical and security review remains outstanding.

No User Interface/API activation, payability, billed estimate, guarantee, wallet action, provider/insurer authority, authorized consent, data-source rights, patient records, AI learning, Stage or Production. /member/costs remains disconnected with all five unknown and disabled awareness action.
