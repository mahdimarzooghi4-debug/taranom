# Sprint 222 — Appointment Request Schedule Provenance

## Goal

Preserve the schedule observation metadata that accompanied a member's selected time slot in the synthetic appointment request journal.

## Delivered

- Added an optional, exact-shape schedule selection to the request command.
- Bound the observation's selected slot reference to the request's slot reference.
- Persisted and returned the observation as part of the existing append-only command record.
- Kept older unresolved browser attempts recoverable by normalizing the missing field to null.
- Added domain and HTTP/PostgreSQL integration coverage for retention, replay, and invalid metadata.

## Safety boundary

The current member demo has no connected schedule source and sends null. CI fixtures use only synthetic references. Source identity, revision, availability, freshness, booking, clinical action, and national release remain unverified or denied.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate gates. No Stage, formal QA, Release, or Production.
