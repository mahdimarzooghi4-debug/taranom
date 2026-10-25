# TRN-TECH-211 — Least-privilege PostgreSQL access for source review

## Boundary
Sprint 201 adds separate data sources for catalog provisioning, runtime recommendation append and read-only inspection. It exercises database grants in ephemeral CI with synthetic-only tables and identities.

## Access contract
- Provisioning alone can create catalog scope and source/package fingerprint rows.
- The writer can read the journal, append event/audit rows, and update only revision/head columns. It cannot alter or delete catalog, source, event or audit history.
- The reader can read the journal and cannot write or change the head.
- Reads use a repeatable-read transaction without row-lock privileges. Appends lock the head and retain revision checks, idempotency, atomic audit writes and read-time chain verification.

## Limits
This verifies PostgreSQL role grants in CI, not a production account or deployment. Test roles use synthetic credentials and internal_test tables. Real reviewer authentication/qualification, independent custody of evidence, and legal/scientific/taxonomy decisions remain unimplemented. All authority flags, clinical use, training and Production remain denied.
