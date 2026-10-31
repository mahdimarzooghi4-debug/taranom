# TRN-TECH-229 — Unverified Schedule Source Metadata

## Purpose

Preserve limited provenance from a future schedule observation without treating the source as authenticated or its claims as verified availability.

## Contract

- A valid observation returns its source reference, source revision, and observation timestamp alongside the claimed slots.
- The metadata property is explicitly named `unverifiedSourceMetadata`.
- Invalid, missing, or unavailable observations return no source metadata and no slots.
- Source authentication, slot availability, booking, clinical action, and national release remain false.
- Caller cancellation propagates and is never converted into an ordinary source outage.

## Boundaries

The metadata is a source assertion only. It is not a signature, authenticated provider identity, freshness approval, capacity proof, rights evidence, booking receipt, or clinical authority. No source is configured in the CI-only HTTP route.

## Verification

MemberGovernance tests cover metadata retention for a valid claim and cancellation propagation. The Kestrel integration asserts that the unconfigured route has no source metadata, no slots, and no authority flags.
