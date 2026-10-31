# Sprint 238 — Anchor inventory checks to the opened store root

## Goal

Ensure each package in a store inventory is verified beneath the same root directory descriptor used to enumerate it.

## Contract

- Inventory passes its already-open root descriptor to each package verifier.
- Package verification duplicates and uses that descriptor instead of resolving the root path again.
- Before returning, inventory checks that the original root path still names the same directory.
- If the root path is replaced during traversal, the whole inventory fails closed.
- No model loading, network access, rights decision, scientific approval, clinical use, or Production authorization is added.

## Verification

A synthetic filesystem test replaces the store-root path with a symlink during package readback and expects ARTIFACT_STORE_INVENTORY_CHANGED.
