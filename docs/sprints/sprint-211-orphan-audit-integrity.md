# Sprint 211 — Reject audit-only journal rows

**State:** Draft implementation stacked after Sprint 210. No merge, Stage, formal QA, Release or Production.

## Goal

Ensure the snapshot validator detects audit records that have no matching review event.

## Implemented

- Read event and audit rows with a full outer join inside the same read-only repeatable-read transaction.
- Reject audit-only rows before deriving any package references.
- Add a PostgreSQL integration fixture with a revision-zero catalog and an orphan audit record.
- Preserve existing checks for event hash, audit identity, command validity, and catalog head consistency.

## Limits

The isolated schema is synthetic test scaffolding. The checks establish database linkage consistency only; they do not authenticate reviewers, establish evidence custody, verify rights or scientific validity, or grant approval.
