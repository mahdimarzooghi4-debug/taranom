# Sprint 212 — Reject linked medical source blobs

**State:** Draft implementation stacked after Sprint 211. No merge, Stage, formal QA, Release or Production.

## Goal

Prevent Verify and report-only reconciliation from treating a symbolic link or reparse-point entry as a package blob.

## Implemented

- Reject linked blob entries before opening them in Verify.
- Report linked entries as UnsafeLinkedBlob and also leave their referenced digest unresolved.
- Add a synthetic symlink test whose target is outside the package root; assert it is never classified as intact.
- Preserve report-only behavior and existing digest/size checks.

## Limits

The store expects an owner-controlled root. Link metadata is checked before opening, but this path-based check is not an OS-level no-follow handle and does not eliminate races with a concurrent process that can replace entries. No file is repaired or deleted.
