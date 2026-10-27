# Sprint 213 — Center appointment queue filters

## Goal

Make the center's read-only appointment request queue easier to inspect without adding acceptance or booking behavior.

## Delivered

- Add optional department and patient-selected-doctor filters to the bounded PostgreSQL-backed center queue.
- Validate filter references, echo normalized filter values, and preserve filters across cursor pagination.
- Add center-panel inputs and strict response validation.
- Add a real Chromium/PostgreSQL regression for conjunctive filtering, 10+1 pagination, and invalid-reference rejection.

## Boundaries

No request acceptance/rejection policy, confirmed booking, availability, identity, licensure, clinical action, Stage, formal QA, Release, Production, or merge.
