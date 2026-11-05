# TRN-TECH-218 — Single-snapshot journal inventory

## Contract

- PgMedicalSourceBlobReconciler opens a PostgreSQL repeatable-read transaction and sets it read-only before querying.
- Catalog heads, source package references, review events, and audit rows are read from that transaction's single database snapshot.
- Every catalog is checked for a complete source set, matching event count/head, sequential revisions, previous/event hash linkage, request UUID linkage, matching audit linkage, valid command digests, and valid source package references.
- Evidence package digests are added only after each event passes validation.
- The database transaction commits before filesystem scanning; the filesystem operation remains report-only.

## Limits

The PostgreSQL snapshot and filesystem scan are not atomic together. A database change after the snapshot can be newer than the blob report. The reader role and internal_test_medical_review_* schema are CI/internal scaffolding; no production storage or access is configured. Hash consistency does not establish authenticity, custody, rights, scientific validity, taxonomy correctness, or acceptance.
