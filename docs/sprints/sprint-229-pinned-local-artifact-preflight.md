# Sprint 229 — Pinned Local Artifact Preflight

## Goal

Let local verification compare the current manifest and artifact set against caller-supplied SHA-256 pins and fail closed on drift.

## Delivered

- Add optional manifest-byte and artifact-set SHA-256 pins to the local verifier and CLI.
- Reject malformed pins and mismatches with stable denial codes.
- Record whether pins were supplied on successful output and denied CLI output.
- Test matching pins, manifest mismatch, artifact-set mismatch, malformed pins, and CLI denial.

## Safety boundary

A caller-supplied pin is only a consistency comparison. It is not a trusted signature or source authentication and does not establish legal/scientific approval or permit loading, training, clinical use, or Production.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate. No Stage, formal QA, Release, or Production.
