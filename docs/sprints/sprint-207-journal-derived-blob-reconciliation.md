# Sprint 207 — Journal-derived source blob reconciliation

**State:** Draft implementation stacked after Sprint 206. No merge, Stage, formal QA, Release or Production.

## Goal

Remove the caller-managed digest-list step from source-store reconciliation by deriving catalog package and review-evidence references from the read-only PostgreSQL journal.

## Implemented

- Read all synthetic review catalog scopes and source package digests using the dedicated reader data source.
- Validate each catalog's event chain, audit linkage, and source linkage through the journal's existing read path.
- Add evidence package digests from the validated review events to the reference set.
- Pass the derived set to the report-only filesystem reconciler without modifying PostgreSQL or blob files.
- Exercise referenced, missing, evidence, and orphan classifications with synthetic bytes and ephemeral PostgreSQL.

## Limits

The adapter targets the existing internal test journal schema. Its PostgreSQL reads and filesystem scan are not one atomic snapshot; findings remain best-effort. A digest identifies bytes only and grants no rights, authenticity, custody, scientific validity, taxonomy acceptance, training, clinical use, or Production authority.
