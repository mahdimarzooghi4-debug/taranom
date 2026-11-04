# TRN-TECH-257 — Appointment HTTP API and central panel grants

## Routes

- `GET /api/v1/centers/{centerRef}/appointments` — center-scoped read.
- `POST /api/v1/centers/{centerRef}/appointment-slots` — center defines a doctor slot.
- `GET /api/v1/member/members/{memberRef}/centers/{centerRef}/appointment-slots` — member-scoped read of open slots.
- `GET /api/v1/member/members/{memberRef}/centers/{centerRef}/appointments` — member's appointments at a center.
- `POST /api/v1/member/members/{memberRef}/centers/{centerRef}/appointments` — member books an open slot.
- `POST /api/v1/member/members/{memberRef}/centers/{centerRef}/appointments/{bookingRef}/cancel` — only the member who owns the booking may cancel.

All business routes require an OIDC token and an exact signed panel-grant claim. A PostgreSQL central grant row must also match issuer, subject, capability, and server-resolved member/center scope; all five current-evidence flags must be confirmed. Missing rows and stale, revoked, mismatched or unbound access deny by default. The URL's `memberRef` is only a scope hint and must match a central grant. The API never trusts a client-provided role or derives a member from the request body.

`/health/ready` is anonymous and returns only a readiness status after a database `SELECT 1`. No identity, member or appointment records are included.

## Boundaries

The API host and database schema are code-only foundations. There is no central-grant administration endpoint, actual Keycloak realm/mapper configuration, BFF/session, deployed service, verified member onboarding, or Figma UI client yet. The grant table has false-by-default evidence flags and starts empty; the API therefore grants no panel access until the approved central administration path and actual issuer claims are configured and independently checked. No sample records are inserted. Insurance, payment, prescriptions, clinical records and medical decisions are outside these routes.
