# TRN-TECH-215 — Appointment Request Domain Contract

**Status:** Draft, synthetic unit-test domain only.

## Contract

- Records a member-scoped *request intent* containing test references for member, center, department, member-requested doctor, and slot.
- Uses immutable revisions and exact-request idempotency; the same request identifier with a different payload is rejected.
- The requested doctor reference remains unchanged by the reducer. The reducer performs no ranking or automatic provider selection.
- The payload has no diagnosis, prescription, or clinical-note field.

## Explicit non-authority

A recorded request is not evidence of a real member action, a provider credential, real slot availability, provider acceptance, confirmed booking, completed visit, or clinical action. The contract exposes no confirmed-booking transition. Identity, provider, center, and slot flags remain false.

## Tests

Synthetic unit tests cover recording, doctor-reference preservation, idempotent replay, conflicting replay, stale revision, invalid references, and absence of clinical payload fields.

## Next integration boundary

A persistent request queue must be implemented separately and must read an authoritative center schedule and verified identity before showing availability or accepting live requests. Provider confirmation and member notifications require their own contract. No live endpoint is wired into the member or center apps by this domain contract.
