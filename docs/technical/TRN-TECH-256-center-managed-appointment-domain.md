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
- Snapshots and command receipts are immutable values. They contain opaque references and appointment times only, not names, contact details, symptoms, diagnosis, insurance or payment data.
- Every successful command stores a SHA-256 fingerprint of its canonical command fields. Repeating the same request ID with the same actor, action and payload replays the original result. Reusing it with different payload or revision returns `ConflictingReplay`. Legacy receipts without a fingerprint fail closed as conflicting replays.
- The PostgreSQL store locks one center's row per command, applies a revision-checked immutable snapshot, and commits only successful state changes. Concurrent commands for one center serialize; independent centers use independent rows.

## Current boundary

The domain and PostgreSQL API support manual slot administration and member booking/cancellation. The hospital-system adapter contract, verified provider directory, panel integrations, and live OIDC remain separate work. This domain does not include provider profile data or clinical decisions.
