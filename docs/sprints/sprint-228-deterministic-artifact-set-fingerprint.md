# Sprint 228 — Deterministic Artifact Set Fingerprint

## Goal

Make repeated offline verification results comparable through one stable fingerprint over the exact verified artifact set.

## Delivered

- Add a versioned, domain-separated SHA-256 over candidate, artifact version, and sorted verified file path/size/digest tuples.
- Make the value independent of manifest row ordering.
- Return null for denied CLI results.
- Test deterministic ordering, version sensitivity, and the CLI output contract.

## Safety boundary

The digest is a comparison identifier only. It is not a signature, source authentication, legal/scientific approval, or permission for model loading, training, clinical use, or Production.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate. No Stage, formal QA, Release, or Production.
