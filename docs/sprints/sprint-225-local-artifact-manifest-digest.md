# Sprint 225 — Bind Artifact Report to Manifest Bytes

## Goal

Identify the exact manifest file bytes used in local artifact verification while preserving the distinction between byte integrity and authenticity.

## Delivered

- Compute manifest SHA-256 from the same pinned bytes used for strict JSON parsing.
- Include the digest in successful local verification reports.
- Explicitly report manifest authenticity as false on success and failure.
- Test digest equality against the exact local manifest bytes.

## Safety boundary

A hash is not a signature, source provenance, license or scientific approval, or permission to load, train, use clinically, or deploy the model.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate. No Stage, formal QA, Release, or Production.
