# TRN-TECH-220 — Appointment request readback recovery

## Purpose
Resolve an uncertain member-side POST using the existing request journal before permitting a write retry.

## Recovery contract
1. Keep the exact synthetic request reference and immutable command after a lost or unverifiable POST response.
2. Do not automatically resend.
3. Offer a read of that exact request reference from TRN-TECH-216.
4. Show a receipt only when the read projection has revision 1, one request, the identical command, and all actor/clinical/release flags false.
5. Permit a manual retry only after a valid read projection proves revision 0, count 0, and no events.
6. Keep retry disabled when the read fails, the payload differs, or the projection is inconsistent.

The server's event journal already enforces idempotency. Readback minimizes unnecessary writes and makes the uncertain result visible to the user without fabricating success.

## Verification
Chromium tests cover both: (a) journal write succeeded but its HTTP acknowledgement was dropped, followed by exact GET reconciliation with no second POST; (b) no journal write occurred, a verified empty read enabled manual resend of the identical body, and exactly one request appeared in the center queue.

## Limits
Test-only synthetic references and isolated CI PostgreSQL. The GET does not establish member identity or ownership. No clinical, booking, availability, licensure, or production authority is added.
