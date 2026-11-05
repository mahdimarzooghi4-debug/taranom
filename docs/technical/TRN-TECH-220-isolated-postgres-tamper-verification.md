# TRN-TECH-220 — Isolated PostgreSQL tamper verification

## Contract

- Each tamper test creates a uniquely named temporary schema containing only the four review-journal relations read by the blob reconciler.
- Fixtures contain synthetic catalog, package, event, and audit values.
- Event hash, audit linkage, audit presence, command validity, and head consistency are independently corrupted.
- The reconciler must throw the expected integrity error before scanning the content store.
- Cleanup drops only the test's own schema, avoiding shared-table trigger changes and cross-test mutation.

## Limits

This validates fail-closed behavior for these synthetic corruption cases only. It does not prove reviewer identity, evidence custody, rights, scientific validity, taxonomy approval, or Production readiness. Database snapshot and filesystem scan remain separate points in time.
