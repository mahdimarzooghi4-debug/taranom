# Sprint 200 — PostgreSQL persistence for medical source review recommendations

**State:** Draft code candidate stacked on Sprint 199 (PR #214); no merge, Stage, formal QA, Release or Production.

## Scope
- Persist synthetic metadata fingerprints and unverified RIGHTS / SCIENCE / TAXONOMY recommendations in an isolated PostgreSQL journal.
- Use row locking, expected revisions, request idempotency, append-only event/audit tables, and read-time hash-chain verification.
- Keep reviewer identity, evidence custody, rights/science/taxonomy approval, training, clinical use and Production authority false.

## Verification
- Add PostgreSQL integration coverage for durable readback, replay, stale and wrong-scope requests, concurrent writes, denied authority flags and atomic rollback.
- Run the canonical full regression on the exact commit. Keep the PR Draft/Open/Unmerged.
- Synthetic references only; no production database, authenticated reviewers or clinical workflow.
