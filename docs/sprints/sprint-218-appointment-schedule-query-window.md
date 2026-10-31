# Sprint 218 — Appointment Schedule Query Window

## Goal

Make future schedule reads explicit about the time interval being requested without inventing provider availability or center policies.

## Delivered

- Require paired `from` and `until` instants with explicit offsets in the synthetic HTTP contract.
- Validate start-before-end at the service boundary.
- Reject a source observation in full if any claimed slot falls outside the requested interval.
- Add unit and real HTTP integration coverage, including offset normalization and malformed windows.
- Keep source, verification, booking, clinical action, and national release disabled.

## Safety boundary

No provider API, credentials, live schedule, local-time policy, capacity rule, slot hold, booking, Stage, formal QA, Release, Production, or national launch is enabled.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate gates.
