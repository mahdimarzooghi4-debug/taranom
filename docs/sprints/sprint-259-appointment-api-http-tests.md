# Sprint 259 — Appointment API HTTP authorization tests

## Goal

Exercise the appointment API through its actual ASP.NET middleware, routes, central PostgreSQL grant lookup, and domain store before wiring browser screens.

## Delivery

- Start the real API host under `WebApplicationFactory` against the disposable PostgreSQL service used by the canonical contract job.
- Replace only token authentication with a test-only handler; leave the actual authorization policies, central grant resolver, database schema, route handlers, and schedule store active.
- Verify anonymous requests receive 401, missing or stale grants and cross-capability/member scopes receive 403, and valid center/member capabilities use their scoped routes.
- Verify member booking and cancellation are persisted through HTTP and a different member scope cannot access the booking.

## Limits and remaining

- This does not verify Keycloak metadata discovery, signed JWT validation against a live issuer, a browser session/BFF, or any real member/center enrollment.
- The central grant registry remains empty outside disposable CI tests; no access is provisioned.
- No merge, Stage, formal QA, release or Production deployment.
