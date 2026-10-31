# TRN-TECH-234 — Strict Local Model Manifest Intake

## Purpose

Provide an executable local-only path for checking the selected internal model's manifest and the bytes it declares. This closes the gap between a Python dictionary preflight and a concrete manifest file supplied by an operator.

## Intake contract

- Read one explicit manifest file through a single file descriptor with no-follow semantics.
- Require a regular, non-empty file no larger than 1 MiB; detect size or metadata changes during the read.
- Decode strict UTF-8 JSON, reject duplicate keys at every object depth, non-finite numbers, malformed JSON, and excessive nesting.
- Pass the parsed object to the existing exact-schema, path-safe, race-resistant artifact byte verifier.
- Offer an offline CLI through `python -m ai.taranom_internal.model_artifact_cli`.

## Output limits

A successful result only states that local bytes match a caller-supplied manifest. The command never loads weights, trains, uses network access, reviews a license, authenticates manifest provenance, or permits clinical or production use. A manifest digest supplied by the operator is not a signature or independent source of truth.

## Verification

Synthetic filesystem tests exercise successful CLI verification, local byte verification, duplicate keys, non-finite values, invalid UTF-8, deeply nested JSON, symlink denial, and size limits. All authority fields remain false.
