# TRN-TECH-221 — Complete event/audit row-set validation

## Contract

- The read-only repeatable-read snapshot includes every row from both the review event and audit relations.
- An event row without an audit row and an audit row without an event row both fail closed.
- Event sequence/hash, audit request/hash linkage, command validity, and head consistency remain checked before blob references are returned.
- A disposable PostgreSQL schema tests an audit-only row without mutating the shared CI schema.

## Limits

These checks establish structural linkage only. They do not establish reviewer identity, evidence custody, rights, scientific validity, taxonomy approval, or Production readiness.
