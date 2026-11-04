# TRN-TECH-259 — Appointment API HTTP authorization tests

`AppointmentApiHttpTests` starts `Taranom.Appointments.Api` with `WebApplicationFactory<Program>` and a disposable PostgreSQL connection. The application uses its real schema bootstrap, `CentralPanelAccessEvidenceSource`, authorization policies, HTTP route handlers, and `PostgresAppointmentScheduleStore`.

A test-only authentication handler substitutes at the authentication boundary and issues synthetic issuer, subject, and capability claims from request headers. It does not replace the authorization handler or central grant lookup. PostgreSQL grants use unique opaque subjects/scopes, and all grant freshness flags are explicit. Coverage checks unauthenticated 401, absent/stale grant 403, capability and member-scope isolation, center slot definition, member slot read, booking, and owner cancellation.

These tests do not assert that a live OIDC provider, signing keys, Keycloak mapper, BFF/browser session, or operational grant administration is ready. The real JWT validation configuration remains covered separately by configuration/security tests; provider integration is still unverified.
