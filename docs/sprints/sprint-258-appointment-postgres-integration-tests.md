# Sprint 258 — PostgreSQL appointment persistence checks

## Goal

Exercise the real appointment PostgreSQL store against a disposable CI database before connecting member and center browser clients.

## Delivery

- Add integration coverage for a schedule surviving a fresh data-source read, booking and member-owned cancellation.
- Verify a non-owner cancellation is denied without changing revision or slot state.
- Verify separate center references remain isolated and stale writes do not persist.
- Race two commands at the same revision and require exactly one write to apply.
- Provision PostgreSQL only inside the canonical GitHub Actions job; use opaque synthetic references and an ephemeral database.

## Remaining

- The endpoint pipeline itself still needs HTTP-level tests for token validation, central grant resolution, request validation, and status codes.
- No central grant administration, Keycloak deployment, browser BFF/session, or member/center API client is provisioned.
- No merge, Stage, formal QA, release or Production deployment.
