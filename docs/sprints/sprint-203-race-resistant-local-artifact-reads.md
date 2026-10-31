# Sprint 203 — Race-Resistant Local Model Artifact Reads

## Goal

Harden the selected Qwen prototype's local artifact preflight against path replacement and ambiguous filesystem paths, using synthetic fixtures only.

## Implementation

- Open the artifact root and each child path through directory descriptors with no-follow semantics.
- Hash the opened file descriptor and reject non-regular files, byte-count mismatches, and file metadata changes observed during hashing.
- Reject empty, dot, and parent path segments before path normalization.
- Extend synthetic filesystem tests for nested symlinks, ambiguous segments, and special files.

## Acceptance evidence

The exact PR head must pass the repository's full GitHub Actions regression workflow. The change remains Draft/Open/Unmerged and does not load model weights or grant training, clinical, release, or Production authority.

## Boundaries

Only `QWEN_QWEN2_5_7B_INSTRUCT` remains the prototype candidate. No model comparison, external model API, real patient data, or real model artifact is introduced. Hash verification is byte integrity checking against a supplied manifest; it is not provenance, authenticity, license, scientific, or safety approval.
