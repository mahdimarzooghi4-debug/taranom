# TRN-TECH-239 — Optional Local Artifact Comparison Pins

## Purpose

Allow an operator or test harness to require that a local manifest and verified artifact set match previously supplied SHA-256 values.

## Contract

The local verification API and CLI accept optional lowercase SHA-256 pins for the exact manifest bytes and deterministic artifact-set fingerprint. Invalid pin syntax is rejected. A manifest mismatch is rejected before parsing or reading model files; an artifact-set mismatch is rejected after full byte and file-set verification. Successful output reports whether each pin was supplied. Denied CLI output retains the supplied flags and emits no verified digest.

## Safety boundary

Pins only compare bytes with values supplied to the same invocation. They do not establish who supplied the expected value, authenticate a source, prove legal rights or scientific validity, or authorize model loading, training, clinical use, or Production.

## Verification

Synthetic tests cover matching pins, malformed pins, manifest mismatch, artifact-set mismatch, CLI success, and fail-closed CLI output.
