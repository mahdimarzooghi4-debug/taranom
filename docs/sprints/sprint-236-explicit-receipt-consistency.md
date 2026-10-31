# Sprint 236 — Explicit receipt consistency status

## Goal

Name the stored-package receipt check according to what it actually establishes.

## Contract

- CLI and inventory results expose `receipt_consistency_verified`.
- The field means the receipt has the expected schema and its recorded verification values match the current local manifest and payload verification.
- It does not mean the receipt is signed, authentic, independently witnessed, or authoritative.
- The result does not establish provenance, usage rights, scientific validity, or permission to load, train, use clinically, or release the model.

## Verification

Synthetic API and CLI tests assert the renamed field is true only after consistency checks pass and remain false on denied inventory entries.
