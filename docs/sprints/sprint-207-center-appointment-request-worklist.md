# Sprint 207 — Read-only center appointment request queue

## Goal

Let the center QA surface inspect synthetic appointment request intents from PostgreSQL while preserving the patient's choice and keeping every booking/clinical authority disabled.

## Implemented scope

- Add a bounded, reader-only, keyset-paginated queue filtered by the synthetic center reference.
- Rebuild and verify each candidate's event and audit history before display; fail closed if the scan is incomplete.
- Expose only synthetic request, center, department, requested-doctor, slot, and revision references. Do not project member references or clinical details.
- Add an opt-in, read-only React QA surface and real Chromium test over the PostgreSQL-backed HTTP host.
- Keep member and center identity, professional license, availability, booking, clinical action, and national release flags false.

## Excluded

No accept/reject action, rescheduling, real patient identity, provider authentication, actual availability, booking, prescription, diagnosis, production endpoint, Stage, formal QA, Release, Production, or merge.
