# TRN-TECH-237 — Closed Local Model Artifact File Set

## Purpose

Ensure the files verified by the offline model artifact preflight are the complete regular-file set beneath the supplied artifact root.

## Contract

After hashing each manifest-declared file through a pinned no-follow descriptor, the verifier walks the artifact root using directory descriptors. It rejects unlisted regular files, symlinks, special files, unsupported descriptor-based scans, and directory trees that change during inventory. The observed file set must exactly equal the manifest paths.

The walk is bounded by the existing manifest file limit for both files and directories. Empty directories are permitted; they are not model payload files.

## Safety boundary

This check closes the local file inventory at verification time. It does not establish who produced the bytes, authenticate the manifest, prove rights or scientific validity, load weights, or grant training, clinical, or Production permission.

## Verification

Synthetic filesystem tests cover undeclared root and nested files and special-file denial, alongside existing byte, path, symlink, size, and digest checks.
