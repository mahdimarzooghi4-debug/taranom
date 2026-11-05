# TRN-TECH-219 — Shared medical review integrity contract

## Contract

- A single internal integrity helper validates journal submissions and persisted commands.
- Journal append/read and report-only blob reconciliation use the same command, source-reference, claim-reference, digest, and event-digest rules.
- Recommendation strings remain limited to request-evidence and non-authoritative accept/reject recommendations.
- Tests cover accepted/rejected values and prove event digest sensitivity to revision and prior hash.

## Limits

Claimed reviewer/evidence references are not authenticated. The helper proves input shape and hash consistency only; it grants no legal, scientific, taxonomy, training, clinical, or Production approval.
