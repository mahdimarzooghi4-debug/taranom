# Sprint 234 — Read-only local model store inventory

## Goal

Add a local operator command that enumerates every top-level item in the model artifact store and independently verifies each content-addressed package before reporting it as verified.

## Contract

- Inventory is read-only and bounded to 4,096 top-level entries.
- An entry is reported verified only after the existing readback verifier confirms package layout, manifest, payload bytes, artifact-set reference, and receipt consistency.
- Unknown names and invalid packages appear as `DENIED` entries; they are never silently skipped.
- Inventory does not load model weights, use network access, authenticate provenance, establish usage rights or scientific validity, or grant training, clinical, or Production authorization.
- The existing explicit per-reference verification and source verification modes remain available.

## CLI

```sh
python -m ai.taranom_internal.model_artifact_cli --store-root /path/to/store --list-store
```

## Verification

Synthetic filesystem tests cover one verified package, one unexpected store item, the CLI output contract, and all authority flags remaining false.
