# TRN-TECH-256 — Center-managed appointment scheduling domain

## Owner decisions carried into the first backend slice

- Centers define their own doctor time slots in Taranom; later hospital-system adapters can provide schedules under separate integration contracts.
- A member books and cancels their own appointment. A center reads its appointment list; this domain does not let a center cancel for a member.
- Appointment cancellation ends that booking. The canceled booking remains in the history while its time slot may be offered again.
- Insurance shares, payment, clinical records, notification delivery and medical decisions are outside this contract.

## Implemented domain invariants

- Each schedule is bound to one center reference. Slot creation rejects a different acting center.
- A doctor cannot receive overlapping active slots at the same center.
- A slot accepts only one active booking. Stale revisions and conflicting idempotency-key reuse are rejected.
- Only the member who booked a slot can cancel it.
- Cancellation preserves the appointment as `CancelledByMember` and reopens the slot for a later independent booking.
- Snapshots and command receipts are immutable values. They contain references and appointment times only, not names, contact details, symptoms, diagnosis, insurance or payment data.

## Current boundary

This is the in-process domain for the first API slice. It is not yet durable storage or an HTTP API and is not connected to either web panel. PostgreSQL transaction enforcement, member-to-subject mapping, current central grants, center-scoped read models, Keycloak configuration, browser session/BFF and UI clients remain the next implementation work. Protected access must stay denied until those server-owned sources are present. No synthetic or demo endpoint is used as production data.
