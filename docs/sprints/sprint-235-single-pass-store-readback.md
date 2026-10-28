# Sprint 235 — Single-pass stored package readback

## Goal

Avoid hashing large local model payloads twice during one stored-package readback while preserving the same manifest and artifact-set pin checks.

## Contract

- The readback verifier hashes the stored manifest and payload once.
- The copy-verification record is derived from that exact verified result with the two explicitly supplied pins marked true.
- Existing receipt equality checks remain strict.
- This is an I/O optimization only. It does not authenticate provenance, rights, scientific validity, or grant loading, training, clinical, or Production authority.

## Verification

Synthetic tests assert that one readback calls the payload verifier once and that the recorded file set and pin flags remain consistent.
