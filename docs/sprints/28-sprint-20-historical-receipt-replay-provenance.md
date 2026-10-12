# TRN-SP-20 — Synthetic historical receipt replay provenance

**Date:** 2026-10-10. **Branch:** `sprint-20-historical-receipt-replay-fence`; parent Sprint 19 branch; intended PR Draft/Open, unmerged.

Business TRN-BIZ-043/044/053/054 → TRN-TECH-029 → Backlog → Sprint 20 → Code → Tests → preliminary Code Review.

- **BL-200:** distinguish an unchanged technical receipt replay from issuer/version lineage mismatch using the pre-existing positive synthetic source check and immutable stored issuer fields.
- **BL-201:** enforce `ConflictingReplay` precedence, read v2 integrity/audit under lock, and preserve rollback/read-only semantics for all replays.
- **BL-202:** exercise replacement issuer, new version, revoked/unavailable source, competing command and parallel source-changed retries on real PostgreSQL; preserve existing receipt/audit count and prior test coverage.
- **BL-203:** add this contract and dedicated inherited 12-job CI workflow to the independent stacked Draft PR; report exact SHA and run evidence when available.
- **BL-204:** source-level code/security review of changed replay branches, no real authority claims; independent professional, security and legal reviews are not waived.

**Never in scope:** human authorization, release of sensitive data, validated professional license, doctor/center approval, real recipient/issuer/IdP, revocation transaction with an external registry, financial transfers, operational UI/API, or Production. The entire feature remains synthetic and non-deployable.
