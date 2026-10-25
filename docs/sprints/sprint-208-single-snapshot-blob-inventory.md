# Sprint 208 — Single-snapshot journal inventory for blob reconciliation

**State:** Draft implementation stacked after Sprint 207. No merge, Stage, formal QA, Release or Production.

## Goal

Read catalog, source, event, and audit references from one read-only PostgreSQL repeatable-read snapshot before scanning the content store.

## Implemented

- Mark the PostgreSQL transaction read-only and use repeatable-read isolation.
- Read all catalog heads, source package references, events, and their audit rows in the same transaction.
- Verify each event's sequence, prior hash, event hash, request identity, audit row, command structure, and source linkage.
- Derive evidence package references from verified event commands and run the existing filesystem report after committing the read transaction.
- Extend synthetic PostgreSQL integration coverage across multiple catalogs, source/evidence blobs, missing blobs, and orphans.

## Limits

The filesystem scan follows the PostgreSQL snapshot and cannot share its transaction. Changes made after the snapshot may appear in the report as they stand during the scan. The adapter remains scoped to the synthetic internal review journal schema. No files or database rows are modified; no rights, scientific validity, custody, or approval is inferred.
