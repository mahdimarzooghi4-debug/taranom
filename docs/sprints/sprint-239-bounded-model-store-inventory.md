# Sprint 239 — Bound local model inventory hashing

## Goal

Bound aggregate payload reads during one local model store inventory.

## Contract

- The inventory payload budget equals the existing per-artifact maximum of 100 GiB.
- Before hashing an entry, readback validates the bounded manifest and sums declared file sizes.
- An entry larger than the remaining budget is reported as DENIED without hashing its payload.
- Verified entries increment the returned verified_payload_bytes total.
- Entries are processed in stable reference order; a denied oversized entry does not consume budget, so a later smaller entry may still be verified.
- No model loading, network access, rights decision, scientific approval, clinical use, or Production authorization is added.

## Verification

A synthetic test lowers the budget to one byte and asserts the package is denied before the payload verifier runs.
