# TRN-TECH-264 — Appointment command fingerprints

The appointment domain stores a deterministic SHA-256 fingerprint on each successful command receipt. The fingerprint is built from a length-prefixed sequence of canonical UTF-8 fields, avoiding delimiter ambiguity. Date/time values use the invariant round-trip format, and revision values use invariant decimal representation.

For slot definition, the fingerprint binds center, actor, slot, doctor, expected revision, start, and end. Booking and cancellation bind center, member, slot, and expected revision. The receipt separately binds request ID, action, actor, and target. Thus a retry succeeds only when both receipt identity and command payload are exact; an existing request ID with any different command returns `ConflictingReplay`.

Older persisted snapshots may contain receipts without the new optional fingerprint property. Deserialization remains compatible; those receipts are treated as conflicting replays because their original payload cannot be reconstructed safely. The API does not apply a write on a conflicting replay.

The fingerprint is an integrity comparison for idempotency. It is not a digital signature, audit attestation, or proof of user identity.
