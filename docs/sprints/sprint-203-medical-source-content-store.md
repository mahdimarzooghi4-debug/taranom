# Sprint 203 — Content-addressed source package storage primitive

**State:** Draft code candidate stacked after Sprint 202. No merge, Stage, formal QA, Release or Production.

## Scope

- Add an explicit-root, content-addressed blob store that writes package streams to a temporary file, hashes bytes while copying, and atomically moves the completed blob to its SHA-256 key.
- Require a caller-supplied positive byte limit, reject empty/oversized input, clean partial files, and deduplicate identical bytes.
- Recompute the digest on verification so at-rest mutation fails closed.
- Exercise storage only with synthetic fixtures under temporary CI directories.

## Limits

This is a storage primitive with a caller-provided root, not a selected production storage service or deployed evidence-custody system. No real medical article or reviewer evidence is added. A content hash does not prove ownership, rights, scientific validity, taxonomy approval or custody. No training, clinical use or Production authority is created.

## Verification

- Tests cover content addressing, duplicate bytes, size/empty rejection, partial-file cleanup, at-rest mutation and unsafe digest paths.
- Keep this PR Draft/Open/Unmerged. CI is not formal QA or human approval.
