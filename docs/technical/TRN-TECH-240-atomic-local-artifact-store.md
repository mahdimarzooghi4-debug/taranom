# TRN-TECH-240 — Atomic Local Artifact Store

## Purpose

Provide an explicit local copy operation for a verified model artifact so the incoming directory can be kept separate from an owner-selected managed store.

## Contract

The caller supplies an existing non-symlink store root. The writer verifies the source and optional comparison pins, rereads the exact manifest bytes, copies each declared file through pinned no-follow descriptors while recomputing size and SHA-256, then rescans the source file set. It writes the manifest and a machine-readable verification receipt into a private temporary directory and atomically renames that directory to a content-addressed path. Existing entries are never replaced. Failed writes remove their private temporary tree when possible.

The stored payload lives under `payload/`; the original manifest is stored as `manifest.json`; the verification receipt is `verification.json`. The receipt preserves the false authority flags.

## Safety boundary

This is local file storage only. It does not load model weights, authenticate a source, prove rights or scientific validity, or grant training, clinical, or Production permission. A stored path is not an immutable or signed record.

## Verification

Synthetic filesystem tests verify atomic publication, content/manifest round-trip, explicit CLI storage, symlink-root denial, duplicate target denial, and false authority flags.
