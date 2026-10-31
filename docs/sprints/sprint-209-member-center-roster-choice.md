# Sprint 209 — Patient selection from center roster

## Goal
Use the selected center's existing versioned synthetic directory to populate specialty and doctor choices in the member appointment request.

## Delivered
- Read the center's current PostgreSQL-backed synthetic snapshot.
- Select a department and read its audited, version-pinned doctor roster.
- Keep doctor selection manual and with the patient; reject free-form doctor references.
- Fail closed when the roster is stale or its source/authority claims do not match.
- Keep slot intent synthetic and unverified.
- Extend the real Chromium -> ASP.NET -> PostgreSQL test through request retry and center queue visibility.

## Boundaries
No center search catalog, live provider identity, licensure, availability, booking, insurance, payment, clinical action, Stage, formal QA, Release, Production, or merge is added.
