# TRN-TECH-256 — Center-managed appointment scheduling domain

## Owner decisions carried into the first backend slice

- Centers define their own doctor time slots in Taranom; later hospital-system adapters can provide schedules under separate integration contracts.
- A member books and cancels their own appointment. A center reads its appointment list; this domain does not let a center cancel for a member.
- Appointment cancellation ends that booking. The canceled booking remains in the history while its time slot may be offered again.
- Insurance shares, payment, clinical records, notification delivery and medical decisions are outside this contract.

## Implemented domain and persistence invariants

- Each schedule is bound to one center reference. Slot creation rejects a different acting center.
- A doctor cannot receive overlapping active slots at the same center.
- A slot accepts only one active booking. Stale revisions and conflicting idempotency-key reuse are rejected.
- Only the member who booked a slot can cancel it.
- Cancellation preserves the appointment as `CancelledByMember` and reopens the slot for a later independent booking.
- Snapshots and command receipts are immutable values. They contain references and appointment times only, not names, contact details, symptoms, diagnosis, insurance or payment data.
- The PostgreSQL store locks one center's row per command, applies a revision-checked immutable snapshot, and commits only successful state changes. Concurrent commands for one center serialize; independent centers use independent rows.

## Current boundary

This slice includes the in-process domain and a PostgreSQL store, but not an HTTP API or PostgreSQL integration tests. It is not connected to either web panel. Member-to-subject mapping, current central grants/revocation, center-scoped read APIs, Keycloak host configuration, browser session/BFF and UI clients remain next. Protected access must stay denied until those server-owned sources are present. No synthetic or demo endpoint is used as production data.
