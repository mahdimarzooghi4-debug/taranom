# Sprint 219 — Unverified Schedule Observation Metadata

## Goal

Retain limited provenance from a future schedule source while preserving the unverified status of every observation.

## Delivered

- Carry source reference, revision, and observation time through the read result.
- Expose the values as `unverifiedSourceMetadata` in the CI-only response.
- Return no metadata for unconfigured, invalid, or unavailable sources.
- Verify caller cancellation propagates.
- Keep all authentication, availability, booking, clinical, and national-release flags false.

## Safety boundary

No source, provider identity, signature, live availability, freshness approval, booking, Stage, formal QA, Release, Production, or national launch is enabled.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate gates.
