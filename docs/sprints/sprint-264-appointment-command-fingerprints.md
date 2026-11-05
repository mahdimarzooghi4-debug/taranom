# Sprint 264 — Appointment command fingerprinting

## Goal

Make idempotency detect reuse of a request ID with any changed command field, rather than comparing only actor, action, and target reference.

## Implemented

- Canonicalize the center, actor, target, expected revision, doctor reference, and appointment time fields for schedule commands.
- Store a SHA-256 fingerprint in each successful appointment command receipt.
- Return `Replayed` only when the request ID and full command fingerprint match.
- Return `ConflictingReplay` for changed dates, doctor, member, or revision under an existing request ID.
- Keep older receipts without fingerprints fail-closed as conflicts.
- Add domain regressions for exact replay, changed slot time, changed doctor, and changed booking revision.

## Verification boundary

The full GitHub regression must compile and run the tests on this exact PR head. No appointment data, credentials, integrations, QA approval, release, or Production access is introduced.
