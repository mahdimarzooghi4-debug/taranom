# Sprint 204 — Offline Candidate Comparison Command

## Goal

Make the Sprint 203 comparison runnable from a controlled local process through a bounded JSON input/output command.

## Delivered

- Added a standard-input/standard-output command for the two selected candidate output batches.
- Bounded input to 8 MiB and rejected duplicate JSON keys, malformed payloads, and non-synthetic fixtures.
- Added subprocess tests and TRN-TECH-214.

## Limits

The command performs no file access, network access, model loading, inference, or training. It evaluates only the previously defined synthetic interaction contract; it makes no medical accuracy claim and grants no clinical or Production permission.

## Acceptance

- Valid bounded synthetic input produces deterministic JSON output.
- Invalid, duplicate-key, or non-synthetic input exits nonzero without echoing supplied content.
- AI tests pass locally and in canonical CI.
