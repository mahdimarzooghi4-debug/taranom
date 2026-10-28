# Sprint 230 — Atomic Local Artifact Store

## Goal

Copy a successfully verified local artifact into an explicitly selected local store without loading it or overwriting an existing content-addressed entry.

## Delivered

- Add descriptor-pinned copy with per-file rehashing into a private temporary directory.
- Preserve exact manifest bytes and write a machine-readable verification receipt.
- Atomically publish under the artifact-set fingerprint; refuse existing target entries.
- Expose the operation through an explicit CLI `--store-root` option.
- Test successful API/CLI storage, symlink-root denial, duplicate-target denial, and authority flags.

## Safety boundary

Storage does not authenticate provenance or approval, prove licensing or scientific quality, load weights, or permit training, clinical use, or Production. No external destination is configured.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate. No Stage, formal QA, Release, or Production.
