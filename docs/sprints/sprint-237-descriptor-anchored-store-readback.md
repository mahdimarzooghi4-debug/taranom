# Sprint 237 — Descriptor-anchored stored-package readback

## Goal

Keep stored-package verification tied to the package directory opened during layout inspection, even if the top-level store entry is replaced while readback runs.

## Contract

- Open the package and payload directories without following symlinks and retain their descriptors through verification.
- Read the manifest and receipt relative to the pinned package descriptor.
- Verify payload files relative to the pinned payload descriptor.
- Recheck package and payload directory identities before returning success; reject a replaced top-level package entry.
- Preserve receipt consistency and all model loading, training, clinical, and Production flags as false.

## Verification

Synthetic filesystem coverage replaces the store reference with a symlink during manifest readback and requires a fail-closed ARTIFACT_STORE_ENTRY_CHANGED result.
