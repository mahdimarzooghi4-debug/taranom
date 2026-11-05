# TRN-TECH-213 — Content-addressed source package storage primitive

## Contract

- `MedicalSourcePackageBlobStore` receives an explicit filesystem root; the caller remains responsible for selecting and protecting that root.
- `StoreAsync` hashes and writes the same bytes incrementally to a unique temporary file, enforces an explicit positive byte ceiling, rejects empty input, flushes and moves the completed file to `<sha256>.blob` under the same root.
- Identical bytes deduplicate by digest. A temporary file is removed on failure. Caller-owned input streams are not disposed.
- `VerifyAsync` accepts only lowercase SHA-256 path keys, returns false for missing blobs, and recomputes the digest before reporting integrity. A mismatch fails closed.

## Limits

The implementation does not authenticate users, establish evidence custody or rights, decide scientific validity, or choose production storage, permissions, retention, backup, key management or deployment. Tests use only synthetic fixtures and ephemeral temporary directories. Production, training and clinical use remain denied.
