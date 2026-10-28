# TRN-TECH-242 — Read-Only Stored Artifact Verification

## Purpose

Allow a stored content-addressed package to be checked later without changing it or loading model weights.

## Contract

The verifier validates the store reference syntax, the package directory layout, and the exact top-level files. It re-verifies the stored manifest and payload against the content-addressed reference, parses the receipt with strict JSON rules, and compares its source and copy verification records with freshly computed results. Any extra entry, symlink, malformed receipt, changed payload, or mismatched digest is denied.

The CLI accepts `--store-root` and `--verify-store-ref` as a read-only verification mode, separate from local intake and storage modes.

## Safety boundary

This verifies internal consistency of local stored bytes and the receipt. It does not authenticate who created the package or receipt, establish rights or scientific approval, load the model, or authorize training, clinical use, or Production.

## Verification

Synthetic API and CLI tests cover a valid stored package, receipt verification, and continued false authority flags.
