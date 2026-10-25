# TRN-TECH-224 — Report temporary medical package ingest artifacts

## Contract

- The read-only blob reconciliation report includes top-level files matching the store's .ingest-*.tmp temporary-file naming convention.
- Each matching entry is reported as TemporaryIngestArtifact, identified by its file name.
- Reconciliation does not open, hash, rename, or delete these entries.
- The report does not infer that an entry is abandoned: it may belong to an ingest that is still running.
- Findings retain deterministic ordering by identifier and kind.

## Limits

This is synthetic-only storage reporting. It does not establish file ownership, liveness, custody, evidence authenticity, rights, patient consent, or approval. Temporary entries are not automatically cleaned up. A future cleanup process would need an independently defined ownership/liveness protocol.
