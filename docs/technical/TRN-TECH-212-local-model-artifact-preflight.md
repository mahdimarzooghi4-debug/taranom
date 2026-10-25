# TRN-TECH-212 — Local Model Artifact Preflight

**Status:** Draft implementation contract for Sprint 202; verification only.

## Purpose

Verify that a locally staged model artifact matches an explicit file manifest before a future internal runtime is configured.

## Contract

- Accept only the selected prototype identifier: Qwen2.5-7B-Instruct.
- Accept a bounded artifact version and a nonempty list of relative file paths, sizes, and lowercase SHA-256 digests.
- Read only files beneath a pre-existing local root; reject path traversal, symlinks, missing files, empty/mismatched sizes, duplicate paths, and digest mismatch.
- Stream file hashing in fixed-size chunks and cap the manifest at 8,192 files and 100 GiB total declared bytes.
- Return `LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED` with all clinical, training, and Production flags false.

## Limits

This is not a license, scientific, authenticity, provenance, or security attestation. A local digest only confirms byte equality to the supplied manifest; it does not establish who supplied the manifest or whether those bytes are safe. No network, model loader, inference, training, or real weights are used. Loading or activation still requires a separately implemented internal runtime and applicable approvals.

## Tests

`tests/ai/test_model_artifacts.py` uses temporary synthetic bytes to cover valid manifests, remote model references, path traversal, symlink, missing-file, duplicate, size, digest, and authority failures.
