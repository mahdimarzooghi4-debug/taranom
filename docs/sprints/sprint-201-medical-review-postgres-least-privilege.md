# Sprint 201 — Least-privilege PostgreSQL roles for source review

**State:** Draft code candidate stacked on Sprint 200 (PR #215); no merge, Stage, formal QA, Release or Production.

## Scope
- Separate provisioning, append-writer and read-only PostgreSQL data sources for the synthetic medical review journal.
- Grant the runtime writer SELECT on journal scope/history, INSERT on event/audit rows, and column-scoped UPDATE on the journal head.
- Keep the reader read-only; keep source catalog provisioning on the owner connection.
- Verify permission denials and successful append/read operations under restricted roles.

## Verification
- Use the ephemeral PostgreSQL integration database and canonical 14-job regression on the exact commit.
- Keep this PR Draft/Open/Unmerged. Roles and credentials are synthetic CI fixtures, not deployed identities.
