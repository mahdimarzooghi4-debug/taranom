# TRN-TECH-238 — Deterministic Local Artifact Set Fingerprint

## Purpose

Provide a stable identifier for comparing repeated offline verification of the same declared model artifact version and verified file bytes.

## Contract

Successful verification returns `artifact_set_sha256`. It is computed only after every declared file passes byte verification and the complete regular-file set matches the manifest. The versioned digest uses domain separation and length-prefixed UTF-8 fields for candidate, artifact version, sorted relative paths, byte counts, and per-file SHA-256 values. Manifest ordering does not affect the result.

Denied CLI output sets the field to null.

## Safety boundary

The fingerprint identifies the locally verified candidate/version/file set for comparison. It is not a digital signature, source authentication, proof of licensing or scientific validity, or authorization to load, train, use clinically, or deploy the model.

## Verification

Synthetic tests verify stable output format, order independence, and sensitivity to the artifact version.
