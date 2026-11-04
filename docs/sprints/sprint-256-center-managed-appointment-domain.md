# Sprint 256 — Center-managed appointment domain

## Goal

Start the real appointment backend from the owner-confirmed workflow: centers configure available time slots, members book or cancel their own appointments, and the center can read its own appointments.

## Delivery in this slice

- Add an immutable .NET domain for center-owned schedule slots and appointment booking/cancellation.
- Add a PostgreSQL store that serializes per-center writes under a row lock and commits only domain-approved revisions.
- Preserve canceled appointments as history while making the slot available for a separate future booking.
- Enforce center scope, member ownership, no overlapping slots per doctor, optimistic revision checks, and idempotency.
- Add xUnit regression cases to the repository's existing .NET contract test project.

## Verification and remaining work

- This workspace does not have the .NET SDK; the exact-head GitHub regression must compile and execute the added tests.
- This slice is not an HTTP API and does not claim that any screen is connected. PostgreSQL integration testing, OIDC-backed member mapping and central grant/revocation source, center read API, BFF/session and both panel clients remain required.
- Draft/Open only. No merge, Stage, formal QA, release or Production access.
