# TRN-TECH-214 — Bind stored source packages to review intake

## Contract

- `OpenAndStorePackageStreamsAsync` stores package streams through the content-addressed store, then pins each computed digest to the synthetic catalog using the provisioning connection.
- `AppendWithStoredPackageAsync` verifies the referenced blob against its SHA-256 before delegating to the existing least-privilege append writer.
- Missing blobs fail before a recommendation is appended. At-rest digest mismatch fails closed. The existing append transaction still couples event, audit and head revision.
- Storage verification is byte integrity only. It does not establish legal or scientific acceptance, source provenance, reviewer qualification, evidence custody, training permission, clinical use or Production authority.

## Operational limits

Filesystem blob writes and PostgreSQL catalog registration do not share a transaction. If registration fails after a blob is written, the content-addressed object can remain unreferenced. No garbage collection, retention schedule, backup policy, production root, access control or deployment is selected. Current integration tests use synthetic bytes and ephemeral CI storage.
