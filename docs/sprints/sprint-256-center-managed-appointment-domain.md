# Sprint 256 — Center-managed appointment domain

## Goal

Start the appointment backend from the owner-confirmed workflow: centers configure slots, members book or cancel their own appointments, and centers read their own appointment schedules.

## Delivered in this sprint

- Added an immutable .NET domain for center-owned schedule slots and booking/cancellation.
- Added a PostgreSQL store that serializes per-center writes under row locks and commits only approved domain revisions.
- Preserved canceled appointments as history while reopening their slots for independent future bookings.
- Enforced center scope, member ownership, no overlapping slots per doctor, optimistic revision checks, and request idempotency.

## Follow-on delivery

Later appointment sprints added PostgreSQL integration tests, OIDC-backed central grants and revocation, authenticated scope discovery, center schedule reads, and the appointment HTTP API. Sprint 264 tightens idempotency to bind the complete command payload and revision.

No merge, Stage, formal QA, release, or Production access is implied by this historical sprint record.
