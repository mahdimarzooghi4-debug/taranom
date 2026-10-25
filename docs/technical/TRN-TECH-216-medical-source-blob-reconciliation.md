# TRN-TECH-216 — Medical source blob reconciliation

## Contract

- MedicalSourcePackageBlobStore.ReconcileAsync accepts caller-supplied referenced SHA-256 values and a positive maximum byte count per blob.
- It enumerates only top-level *.blob files and streams each candidate through SHA-256.
- Findings distinguish ReferencedIntact, OrphanIntact, MissingReference, Tampered, ExceedsSizeLimit, and InvalidBlobName.
- Invalid reference digests fail before scanning. Results are stable-sorted by identifier and finding kind.
- The operation is read-only: it does not modify, delete, or repair any file.

## Limits

The report reflects a best-effort point-in-time filesystem view and is not atomic with PostgreSQL. The caller supplies the reference set, which must include the desired catalog package and review evidence digests. No identity, custody, legal rights, scientific validity, taxonomy correctness, retention, or approval is inferred. Tests use synthetic bytes and temporary directories only; no Production authority is introduced.
