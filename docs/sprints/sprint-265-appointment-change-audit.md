# Sprint 265 — Appointment change audit

## Goal

Attribute every successful schedule change to the authenticated OIDC identity and commit its audit event atomically with the appointment state.

## Implemented

- Require an authenticated audit actor for every schedule mutation.
- Append a PostgreSQL event for slot definition, member booking, and member cancellation.
- Capture issuer, subject, center, request ID, action, opaque resource references, command fingerprint, resulting revision, and database time.
- Commit snapshot and event in one transaction; audit failure aborts the schedule change.
- Keep exact replays from producing duplicate audit rows.
- Add HTTP/PostgreSQL coverage for event action, actor identity, revision order, request IDs, and replay behavior.

## Verification boundary

The full GitHub regression must pass on the exact PR head. This adds an application-level write path only; database privilege separation, independent audit storage, retention policy, read-access logging, Stage, formal QA, release, and Production are not included.
