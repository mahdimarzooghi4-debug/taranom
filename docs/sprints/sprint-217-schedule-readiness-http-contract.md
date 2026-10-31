# Sprint 217 — Schedule Readiness HTTP Contract

## Goal

Create the technical HTTP boundary for a future schedule source while no provider is connected.

## Delivered

- Add a CI-only read route scoped to synthetic centers.
- Accept optional department and patient-selected doctor filters.
- Return an explicit unconfigured-source outcome and an empty claim list.
- Reject unexpected or repeated filter parameters.
- Add a real Kestrel integration test for the response and input validation.

## Safety boundary

No external source, credentials, real schedule, availability claim, slot hold, booking, member identity, clinical action, Stage, formal QA, Release, Production, or national launch is enabled.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review are separate gates.
