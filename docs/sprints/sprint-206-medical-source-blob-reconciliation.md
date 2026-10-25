# Sprint 206 — Medical source blob reconciliation

**State:** Draft implementation stacked after Sprint 205. No merge, Stage, formal QA, Release or Production.

## Goal

Provide a report-only comparison between digest-named files in the medical package store and a caller-supplied set of catalog and review-event SHA-256 references.

## Implemented

- Classify referenced intact blobs, unreferenced intact blobs, missing references, content mismatches, size-limited blobs, and invalid .blob filenames.
- Validate every supplied digest before scanning.
- Hash file contents as streams with a caller-supplied maximum byte limit.
- Return deterministic findings without changing the store.

## Boundaries

This is a point-in-time filesystem report, not a durable inventory or database transaction. The caller must supply the complete set of catalog and evidence digests to compare. A finding does not authenticate evidence, establish custody or rights, verify scientific or taxonomy claims, or grant acceptance. No blob is deleted, repaired, renamed, or assigned a retention policy. CI fixtures are synthetic only.
