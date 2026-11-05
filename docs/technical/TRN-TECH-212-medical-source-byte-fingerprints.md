# TRN-TECH-212 — Byte-derived medical source package fingerprints

## Contract

- The synthetic journal can open a catalog from source package streams through `OpenFromPackageStreamsAsync`. Callers supply source refs and streams, not claimed package hashes.
- SHA-256 is computed incrementally from the exact stream bytes. A positive per-package maximum is mandatory; empty input or a stream crossing the bound fails closed before `OpenAsync` writes catalog state.
- The verifier does not close caller-owned streams and does not retain or persist package bytes. The existing database stores only source reference and computed digest.
- Stream hashing proves byte correspondence only. It does not prove provenance, ownership, rights, license permission, scientific quality, taxonomy validity, reviewer identity, evidence custody or any approval.

## Boundary

The implementation and PostgreSQL tests use synthetic `test:` source refs and ephemeral CI roles. It is not a production upload service or evidence-custody system. No real article or medical data is introduced. Reviewer authentication, legal/scientific decisions, taxonomy authority, training, clinical use and Production remain unavailable and denied.
