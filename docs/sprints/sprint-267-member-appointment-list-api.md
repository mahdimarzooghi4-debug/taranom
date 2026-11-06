# Sprint 267 — Member appointment list API

## Goal

Let an authenticated member app read its own booked and cancelled appointments across centers without supplying another member's identifier or relying on a provider-directory endpoint.

## Delivered

- Add an authenticated GET endpoint at /api/v1/me/appointments.
- Resolve appointment ownership from the authenticated OIDC issuer and subject through the current, active MemberAppointmentsManage grant rows.
- Return only booking rows whose member reference is in a current grant for that subject; do not accept memberRef or centerRef as request selectors.
- Return opaque booking/member/center references, state, start time, and schedule revision with Cache-Control no-store.
- Add HTTP regression coverage for own booking visibility, exclusion of another member's booking, and persistence of the cancelled state in the member's own list.

## Boundaries

The endpoint does not provide provider search, provider profiles, city/specialty data, or booking-center discovery. It returns no patient name or clinical data. No client can see a booking solely by changing a URL parameter. OIDC issuer and subject authentication plus the current central grant registry remain required. No merge, Stage, formal QA, release, or Production access is implied.
