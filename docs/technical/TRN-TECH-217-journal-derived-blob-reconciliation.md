# TRN-TECH-217 — Journal-derived source blob reconciliation

## Contract

- PgMedicalSourceBlobReconciler uses the restricted review-reader Npgsql data source and never writes to PostgreSQL.
- It collects all catalog scopes and registered source package SHA-256 values from the synthetic review journal.
- For each scope it calls the existing journal read path, which checks event ordering, previous/event hashes, audit linkage, request identity linkage, and source package linkage.
- It adds the verified event commands' evidence package SHA-256 values, then runs the read-only filesystem report.
- The caller no longer supplies or can accidentally omit the reference list.

## Limits

The adapter is scoped to internal_test_medical_review_* tables and the configured reader role. Database catalog collection, per-catalog journal verification, and filesystem scanning are not one distributed snapshot. The report does not establish evidence authenticity, custody, rights, scientific quality, taxonomy correctness, or acceptance. Synthetic CI fixtures only; no deletes, repairs, retention decisions, or Production access.
