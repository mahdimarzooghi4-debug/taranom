# TRN-TECH-213 — Race-Resistant Local Artifact Reads

**Status:** Draft implementation contract for Sprint 203; local byte verification only.

## Change

The Qwen prototype artifact verifier now opens each path beneath a pinned root directory descriptor. Every intermediate component uses a no-follow directory open, and the final file is opened without following symlinks. Hashing reads from that same descriptor, rejects non-regular files, bounds the byte count to the manifest size, and checks file metadata before and after reading.

Manifest paths reject empty, dot, and parent segments before normalization. This prevents ambiguous forms such as `weights//part` and `weights/./part` from being silently rewritten.

## Verification

Synthetic filesystem tests cover valid bytes, final and nested symlinks, traversal and ambiguous paths, missing and special files, size and digest mismatches, duplicate entries, and the existing authority-deny flags. CI is the execution environment for this repository's complete regression suite.

## Limits

The verifier still does not establish who supplied a manifest, whether an artifact is licensed or scientifically valid, or whether the bytes are safe to load. It does not download or load weights, perform inference, train a model, or enable clinical or Production use. Descriptor-relative no-follow operations are required; unsupported platforms fail closed.
