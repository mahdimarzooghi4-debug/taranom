# Sprint 206 — Persistent synthetic appointment request

## Goal

Move the synthetic appointment request intent from an in-memory domain contract to a restricted, append-only PostgreSQL journal exercised through the real local HTTP test host.

## Implemented scope

- Persist revisioned synthetic requests and verify event/audit lineage on each read.
- Isolate appointment request writer and reader credentials and verify their database privileges.
- Expose opt-in local CI-only read/write routes for test references.
- Reject unexpected payload fields and non-local origins; keep authentication, license, availability, booking, clinical action, and national release flags false.
- Add a real HTTP/PostgreSQL integration test for persistence, retries, collisions, stale revisions, and fail-closed boundaries.

## Excluded

No real member data, provider schedule, booking confirmation, payer calls, prescription, diagnosis, application integration, Stage, formal QA, Release, Production, or merge.
