# Sprint 212 — Read-only member appointment history

## Goal

Let the member demo re-read its synthetic request intents from the isolated journal after a page reload.

## Delivered

- Add a bounded, stable, cursor-paginated PostgreSQL read filtered by synthetic member reference.
- Rebuild and verify audit/event history before returning each row; reject invalid cursors, references, and query fields.
- Add a member-demo history panel that validates contract, source, pagination, exact reference filtering, and deny-only flags before rendering.
- Add real browser and HTTP coverage for eleven-plus-one pagination and isolation from a different synthetic member reference.

## Boundaries

The synthetic reference is not identity or authorization. No acceptance, rejection, appointment booking, availability, clinical action, insurer connection, Stage, formal QA, Release, Production, or merge.
