# Sprint 231 — Read-Back Verification for Local Artifact Store

## Goal

Prevent publishing a stored local artifact until the temporary copy has itself passed the same strict manifest, file-set, size, and SHA-256 checks.

## Delivered

- Reopen the exact manifest and payload from the private temporary store directory.
- Require both original manifest and artifact-set pins to match on read-back.
- Include source and copy verification inventories in the stored receipt and result.
- Extend synthetic store tests to compare the two inventories.

## Safety boundary

Read-back is a local byte comparison only. It does not authenticate source provenance or approval, prove legal/scientific validity, load weights, or permit training, clinical use, or Production.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate. No Stage, formal QA, Release, or Production.
