# TRN-TECH-212 — Local Model Artifact Preflight

**Status:** Draft implementation contract for Sprint 202; verification only.

## Purpose

Verify that a locally staged model artifact matches an explicit file manifest before a future internal runtime is configured.

## Contract

- Accept only the selected prototype identifier: Qwen2.5-7B-Instruct.
- Accept a bounded artifact version and a nonempty list of relative file paths, sizes, and lowercase SHA-256 digests.
- Read only files beneath a pre-existing local root through directory descriptors with no-follow opens; reject path traversal, ambiguous path segments, unsafe symlink or non-directory path components, missing files, special files, duplicate paths, and size or digest mismatches.
- Hash the already-open file descriptor, bound bytes read by the declared size, and reject metadata changes observed during the read. This prevents path replacement between validation and opening from redirecting verification.
- Stream file hashing in fixed-size chunks and cap the manifest at 8,192 files and 100 GiB total declared bytes.
- Return `LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED` with all clinical, training, and Production flags false.

## Limits

This is not a license, scientific, authenticity, provenance, or security attestation. A local digest only confirms byte equality to the supplied manifest; it does not establish who supplied the manifest or whether those bytes are safe. No network, model loader, inference, training, or real weights are used. Loading or activation still requires a separately implemented internal runtime and applicable approvals.

## Tests

`tests/ai/test_model_artifacts.py` uses temporary synthetic bytes to cover valid manifests, remote model references, ambiguous paths, nested and final symlinks, special files, missing-file, duplicate, size, digest, and authority failures.
