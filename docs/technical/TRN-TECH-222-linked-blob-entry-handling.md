# TRN-TECH-222 — Linked blob entry handling

## Contract

- Verify rejects a package path marked with FileAttributes.ReparsePoint before opening it.
- Reconciliation reports such an entry as UnsafeLinkedBlob and does not count the digest as a valid stored blob.
- A referenced linked entry therefore also remains a MissingReference.
- A synthetic test uses a link targeting a file outside the package root and checks that its bytes are not accepted as the referenced package.

## Limits

The package root must be controlled by its owner. This metadata check is path-based and does not provide race-free OS-level no-follow semantics against a concurrent writer. It does not modify the link or target and does not establish custody or authenticity.
