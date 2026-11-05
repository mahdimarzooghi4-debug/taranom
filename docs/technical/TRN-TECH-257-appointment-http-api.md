# TRN-TECH-257 — Appointment HTTP API and central panel grants

## Routes

- `GET /api/v1/centers/{centerRef}/appointments` — center-scoped appointment read.
- `POST /api/v1/centers/{centerRef}/appointment-slots` — center defines a doctor slot.
- `GET /api/v1/member/members/{memberRef}/centers/{centerRef}/appointment-slots` — member-scoped read of open slots.
- `GET /api/v1/member/members/{memberRef}/centers/{centerRef}/appointments` — member's appointments at a center.
- `POST /api/v1/member/members/{memberRef}/centers/{centerRef}/appointments` — member books an open slot.
- `POST /api/v1/member/members/{memberRef}/centers/{centerRef}/appointments/{bookingRef}/cancel` — only the member who owns the booking may cancel.

Each business route requires an authenticated OIDC identity and an active central grant for the exact issuer, subject, capability, and scope. Dynamic capabilities are not copied into the OIDC token. The API reads the grant registry for each request, so a manager's grant or revocation is effective on the next request without a token refresh. The member or center in the path is only a scope hint and must exactly match the resolved grant and resource owner. Stale evidence, revoked or missing grants, mismatched scope, and unbound routes deny by default.

`GET /api/v1/me/appointment-scopes` returns the caller's active appointment scopes from the same registry. It requires authenticated identity, returns no names or clinical data, and is marked `Cache-Control: no-store`.

`/health/ready` is anonymous and returns only a readiness status after a database `SELECT 1`.

## Boundaries

The API host and database schema are code foundations. Initial center-manager grants still require an approved bootstrap path. A verified staff directory and invitation/onboarding flow, browser BFF/session, connected frontends, and live OIDC issuer verification remain outstanding. The grant table starts empty and false-by-default evidence prevents accidental access. No sample records are inserted. Insurance, payment, prescriptions, clinical records, and medical decisions are outside these routes.
