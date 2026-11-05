# Sprint 202 — Byte-derived medical source package fingerprints

**State:** Draft code candidate stacked after Sprint 201. No merge, Stage, formal QA, Release or Production.

## Scope

- Compute SHA-256 from caller-provided package streams instead of accepting a package digest claim at catalog opening.
- Require a caller-supplied positive per-package byte limit; reject empty streams and streams that exceed it before catalog scope is created.
- Keep the stream open and avoid copying or persisting package bytes.
- Keep existing synthetic-only source references, least-privilege roles, and false identity/evidence/rights/science/taxonomy/training/clinical/production flags.

## Verification

- Real PostgreSQL integration test opens a catalog from synthetic package bytes and records a recommendation against the computed digest.
- Negative PostgreSQL integration test proves an oversized stream creates no catalog head.
- Stream-level tests reject empty input and invalid limits.
- Keep the PR Draft/Open/Unmerged. CI is the integration verification gate for this code candidate, not formal QA or human approval.
