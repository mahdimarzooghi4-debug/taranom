# TRN-TECH-232 — Unverified Schedule Provenance on Appointment Requests

## Purpose

Carry the schedule observation that a synthetic member demo used when recording an appointment request. The captured values support traceability across the request journal; they are not evidence that a schedule source, provider, or time slot is authentic or current.

## Command contract

SyntheticAppointmentRequestCommand accepts an optional unverifiedScheduleSelection object with exactly these fields:

| Field | Meaning |
| --- | --- |
| sourceRef | Synthetic reference for the source that supplied the observation |
| sourceRevision | Synthetic revision or version label supplied with that observation |
| observedAt | Timestamp claimed for the observation |
| slotRef | Selected synthetic slot; must equal the command's slotRef |
| startsAt | Claimed beginning of the selected interval |
| endsAt | Claimed end of the selected interval |

The field is nullable and must be present in the strict HTTP command. null means the demo had no connected schedule observation. A non-null object must have the exact shape above; all references must use bounded test: identifiers, timestamps must be non-default, and the interval must end after it starts.

## Persistence and replay

The command journal stores the serialized command and digest, so the optional observation is retained in the existing append-only event without a new database column. Readback returns the same observation. Exact retries include the same observation; changing its source, revision, times, or selected slot under the same request ID is a conflicting replay.

Older unresolved browser attempts that lack the new field are normalized to explicit null before reconciliation. This lets the demo verify or retry the same older command because the server deserializes a missing optional value as null.

## Safety boundary

The values are caller-supplied claims. The implementation does not authenticate the source, verify its revision, determine freshness, confirm availability, reserve a slot, create a booking, or authorize clinical action. The current member demo has no connected schedule source and therefore sends null. Synthetic non-null values exist only in CI integration fixtures.

## Verification

- Domain tests cover accepted retention, replay conflicts, reference mismatch, and invalid intervals.
- HTTP integration tests verify strict nested shape, null behavior, and persistence/readback through isolated PostgreSQL.
- The projection continues to report slot availability, booking, and clinical action as false.

The contract advances only the synthetic appointment-request write/read response to TRN-TECH-232. Center and member list contracts are unchanged.
