# TRN-SP-11 — Internal unverified evidence queue read model

Business TRN-BIZ-053/054, TRN-BIZ-043/044 → Technical TRN-TECH-020 → Backlog → Sprint 11 → Code → Code Review.

- BL-110: make the existing synthetic pending-review work items discoverable as a **read-only per-center** PostgreSQL query, never as approved physicians or managers.
- BL-111: support filtering representative vs physician and distinguish qualification vs independent Taranom service-contract claims.
- BL-112: deterministic typed keyset pagination scoped to exact center and subject filter; reject cross-center reuse and malformed query/cursor; disclose non-snapshot behavior.
- BL-113: verify returned rows against accepted event digest, review work-item, audit and current case revision, fail closed when inconsistent.
- BL-114: run review queue using read-only PostgreSQL test role; prove write attempts rejected; cover cases with real PostgreSQL tests.
- BL-115: inherit 12 actual CI jobs, fix exact-head failures and record Code Review limitations without changing previous safety contracts.

No human reviewer permission schema, status transition, decision, approval, real license/identity/contract verification, real document storage, operational API, center UI connection, PHI, financial integration, network manager or Production authorization. Synthetic-only proof, previous data interlock remains closed. Stacked Draft PR, no Ready/Merge/Stage/QA/Release/Production.
