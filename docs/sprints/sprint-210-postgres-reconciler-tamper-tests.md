# Sprint 210 — PostgreSQL reconciler tamper tests

**State:** Draft implementation stacked after Sprint 209. No merge, Stage, formal QA, Release or Production.

## Goal

Exercise the report-only reconciler against corrupted persisted snapshots using isolated PostgreSQL objects.

## Implemented

- Create a unique disposable schema per test, separate from shared CI journal tables.
- Seed synthetic catalog/source/event/audit rows with targeted corruption.
- Verify fail-closed handling for invalid event hashes, mismatched or missing audit rows, invalid commands, and divergent catalog heads.
- Keep reconciliation read-only; each test removes only its own disposable schema.

## Limits

Fixtures are synthetic and test-only. Passing tests establish only the specified database consistency behavior; they do not authenticate reviewers, establish custody, rights, scientific validity, taxonomy correctness, or approval. Filesystem scanning is not atomic with the database snapshot.
