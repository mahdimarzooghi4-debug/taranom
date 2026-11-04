# Sprint 257 — Appointment HTTP API

## Goal

Expose the center-managed appointment domain through an OIDC-protected .NET API and derive center/member scope only from the central PostgreSQL access registry.

## Delivery

- Add a real ASP.NET API host with the OIDC JWT validation boundary.
- Add separate center and member capabilities, exact scoped authorization, and PostgreSQL-backed appointment routes.
- Permit members to read open slots and their own appointments, book, and cancel. Permit a center to define slots and read only its own appointments.
- Keep access evidence absent by default; empty/unconfirmed central grant rows never allow access.
- Compile the API project from the contract test project and preserve deny-by-default behavior.

## Remaining

- PostgreSQL integration tests of new API routes and concurrency.
- Central administrator grant/revocation workflow with auditable lifecycle.
- Keycloak realm and claim mapper configuration, verified identity onboarding, browser BFF/session, and member/center frontend clients.
- No merge, Stage, formal QA, release or Production deployment.
