# TRN-TECH-226 — Appointment Schedule Source Port

## Purpose

Define a provider-neutral .NET boundary for reading schedule observations when an authorized center schedule source is introduced later.

## Contract

- Queries carry the center and optional department and patient-selected doctor references already used by the appointment request flow.
- A future adapter returns its source reference, source revision, observation time, matching query scope, and ordered slot claims.
- The read service rejects malformed, duplicate, unordered, or scope-mismatched source results.
- With no configured adapter, the outcome is `SourceNotConfigured` and the slot list is empty.
- A received observation remains a source claim. This contract does not authenticate the source, verify capacity, confirm a booking, or allow clinical action.
- Source exceptions fail closed; caller cancellation propagates.

## Boundaries

No external API, credentials, provider identity, licensing evidence, live schedule, slot hold, booking state, member authentication, or production route is added. Adapter details and authorization remain pending the owner-provided source and agreement.

## Verification

The member-governance test project covers the unconfigured state, valid but unverified claims, wrong-scope results, malformed slot claims, and source failure.
