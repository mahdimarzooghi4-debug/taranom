# Sprint 202 — Local Model Artifact Preflight

## Goal

Add a working preflight for model files already present in a controlled internal directory, before any future runtime can load them.

## Delivered

- Added a bounded manifest verifier for the selected Qwen2.5-7B-Instruct prototype.
- Verify file sizes and SHA-256 digests locally, streaming bytes without network access.
- Reject unsafe paths, symlinks, missing or changed files, duplicate entries, and unsupported candidates.
- Add synthetic filesystem tests and TRN-TECH-212.

## Limits

No model weights are included, downloaded, or loaded. Digest verification does not establish licensing, scientific validity, provenance, authenticity, or approval. Clinical use, training, model activation, Stage, Release, and Production remain disabled.

## Acceptance

- All AI tests pass locally and in canonical CI.
- Valid local bytes produce a verification-only result.
- Invalid manifests and filesystem paths fail closed.
