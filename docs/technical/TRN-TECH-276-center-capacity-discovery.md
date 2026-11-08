# TRN-TECH-276 — Capacity-backed center discovery (first live booking entry)

Figma references: 71:59 (start booking), 74:2 (provider results), 77:2 (slot selection).
This is an intentionally narrower **center-capacity search**, not a clinical provider directory.

## Delivered
- GET /api/v1/me/available-appointment-centers joins the caller's current
  MemberAppointmentsManage grant to actual PostgreSQL schedules in one SQL
  statement. Missing, revoked or stale grants fail closed with 403; missing
  authentication is 401. Results are no-store.
- Only future Open slot inventory is returned, aggregated by opaque center
  reference (revision, open slot count, earliest available time), ordered
  deterministically and capped at 50 centers.
- /patient-app/booking and /patient-app/providers render this read-only
  selection. An actual returned center enters the existing revision-protected
  /patient-app/slots?center=... booking flow. No new booking/write endpoint.
- Patient home now links into the connected selection rather than the sample
  city/specialty page. Missing-center slot entry offers a real selection link.

## Invariants
- No invented doctors, center names, specialties, locations, clinician
  licenses, qualifications, identity verification or hospital affiliation.
- An appointment slot is not a verified physician registry record. Browsing is
  not a capacity hold or reservation; revalidation occurs at booking.
- A confirmed direct booking does not establish a separate center
  acceptance/approval or a pending appointment-request workflow.
- The response does not include patient identity or private bookings. The
  caller's member grant is checked on every request; it is never inferred
  from query parameters or client-side role strings.
- Member UI does not store tokens, sensitive data or commands in localStorage.
- No live login, issuer bootstrap, clinical system integration, Stage,
  Production, release approval or merge is performed.

## Verification
- PostgreSQL HTTP test covers 401, 403, stale grants, authentic slot
  discovery and disappearance after booking.
- Five Chromium browser tests cover entry from home, center filtering,
  missing login, access revocation, malformed/empty responses and mobile
  assets/overflow. Browser API replies remain intercepted, not deployed E2E.
- Next: authoritative center/provider identity registry and qualification
  evidence; city/specialty search then needs an explicit verified directory
  contract. Browser OIDC session/BFF/bootstrap is independently blocked
  on real identity infrastructure.
