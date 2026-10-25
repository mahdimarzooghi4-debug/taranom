# Sprint 209 — Shared review integrity contract

**State:** Draft implementation stacked after Sprint 208. No merge, Stage, formal QA, Release or Production.

## Goal

Keep journal reads and report-only blob inventory on one source of truth for validating review commands, synthetic references, and event digests.

## Implemented

- Extract shared validation for review submissions and commands, lane/recommendation enums, claimed reference syntax, synthetic source references, and lowercase SHA-256 values.
- Share event digest construction between journal append/read and blob reconciliation.
- Add focused tests for accepted and rejected contract values and digest binding to revision and previous hash.

## Limits

Reviewer and evidence references remain claims. Hash integrity does not establish identity, custody, rights, scientific validity, taxonomy correctness, or acceptance. Existing storage stays synthetic and report-only; no Production access or approval is introduced.
