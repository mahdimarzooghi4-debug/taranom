# Sprint 221 — Responsive Schedule Read Cancellation

## Goal

Ensure an aborted caller does not remain blocked by the provider-neutral schedule read service.

## Delivered

- Check cancellation before request validation and source configuration.
- Await source reads with caller cancellation.
- Preserve cancellation as a distinct control outcome instead of reporting source outage.
- Test an adapter that ignores cancellation and completes later.

## Safety boundary

No provider, credentials, live availability, booking, clinical action, Stage, formal QA, Release, Production, or national launch is enabled. The source interface remains read-only.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate gates.
