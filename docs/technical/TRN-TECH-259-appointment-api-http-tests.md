# TRN-TECH-259 — Appointment API HTTP authorization tests

`AppointmentApiHttpTests` starts `Taranom.Appointments.Api` with `WebApplicationFactory<Program>` and a disposable PostgreSQL connection. The application uses its real schema bootstrap, `CentralPanelAccessEvidenceSource`, authorization policies, HTTP route handlers, and `PostgresAppointmentScheduleStore`.

A test-only authentication handler substitutes at the authentication boundary and issues synthetic issuer and subject identity claims. It issues no capability or scope claims. The API resolves authorization through the same current central grant registry used by production routes. PostgreSQL grant rows use unique opaque subjects/scopes, and evidence flags are explicit. Coverage checks unauthenticated 401, absent/stale grants 403, capability and member-scope isolation, center slot definition, member slot read, booking, owner cancellation, center manager grant/revoke, idempotent replay/conflict, and immediate denial after revocation.

These tests do not assert that a live OIDC provider, signing keys, BFF/browser session, or operational manager bootstrap is ready. The JWT validation configuration remains covered separately by configuration/security tests; live provider integration remains unverified.
