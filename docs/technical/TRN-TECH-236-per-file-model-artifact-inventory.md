# TRN-TECH-236 — Per-File Local Artifact Verification Inventory

## Purpose

Make successful local model artifact checks explain exactly which files were opened and matched, including their observed byte counts and SHA-256 values.

## Output contract

The local verifier adds `verified_files`, ordered according to the manifest. Each row contains the normalized relative path, the digest computed from the opened file descriptor, and the byte count read. Rows are added only after file type, size, stable-read metadata, byte count, and digest checks all pass. Any failure denies the overall verification result.

The inventory supplements `manifest_sha256`, which identifies the exact manifest bytes consumed by the CLI. `manifest_authenticity_verified` remains false.

## Safety boundary

The inventory is byte-integrity evidence for files available on the local filesystem. It does not authenticate the manifest or model source, prove licensing or scientific quality, load the model, or grant training, clinical, or Production permission.

## Verification

Synthetic filesystem tests check a successful one-file inventory and continue to exercise multi-file parsing, path traversal, symlinks, size drift, digest mismatch, and changed-during-read denials.
