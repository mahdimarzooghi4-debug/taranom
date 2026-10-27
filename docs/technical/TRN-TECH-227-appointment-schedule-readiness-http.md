# TRN-TECH-227 — Appointment Schedule Readiness HTTP Contract

## Purpose

Expose a local technical read route that a future member or center integration can call to inspect whether a schedule source is configured for the requested center and optional patient-selected filters.

## Contract

- The route is registered only in the CI-ephemeral appointment QA host.
- It accepts a synthetic center reference and optional synthetic department and requested-doctor references.
- Unknown or repeated query fields are rejected.
- With the current unconfigured reader, it returns `SourceNotConfigured`, an empty source-claim list, and false authority flags.
- The endpoint is read-only and propagates request cancellation to the schedule read service.

## Boundaries

No source API, credentials, live availability, date/time interpretation, slot reservation, booking, member authentication, clinical action, or production route is connected. The route does not make source claims authoritative.

## Verification

The real Kestrel/PostgreSQL integration test checks the unconfigured result, echoed filters, empty source claims, deny-only flags, and rejection of unknown or repeated query parameters.
