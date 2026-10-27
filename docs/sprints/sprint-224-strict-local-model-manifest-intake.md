# Sprint 224 — Strict Local Model Manifest Intake

## Goal

Turn the selected local model artifact preflight into a bounded command that reads an actual manifest file and validates the declared local bytes without loading the model.

## Delivered

- Added a no-follow, descriptor-pinned local manifest reader with a 1 MiB bound and read-stability checks.
- Rejected duplicate JSON keys, non-finite values, invalid UTF-8, malformed/deeply nested JSON, symlinked manifests, and oversized files.
- Added an offline CLI for the single selected Qwen candidate and the existing local artifact verifier.
- Added synthetic filesystem and CLI tests.

## Safety boundary

This verifies local byte equality against a supplied manifest only. It does not establish manifest authenticity, source provenance, license rights, scientific validity, model quality, clinical permission, training permission, or Production approval. No network calls or model loading are performed.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate. No Stage, formal QA, Release, or Production.
