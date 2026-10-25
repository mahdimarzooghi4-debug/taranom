# Sprint 204 — Bind stored source packages to review intake

**State:** Draft code candidate stacked after Sprint 203. No merge, Stage, formal QA, Release or Production.

## Scope

- Add a journal entry point that stores each synthetic package stream in the content-addressed blob store and binds its computed digest to the catalog source rows.
- Require blob integrity verification before appending a review recommendation.
- Reject missing or tampered package blobs before an event or audit revision is written.
- Verify the storage-to-PostgreSQL-to-review path with real ephemeral PostgreSQL and temporary filesystem integration tests.

## Limits

This proves byte integrity and catalog binding only. It does not prove provenance, ownership, rights, license permission, scientific validity, taxonomy correctness, reviewer identity, evidence custody or approval. Blob writes and PostgreSQL catalog registration are not one distributed transaction; a later database failure may leave an unreferenced content-addressed blob. Retention and reconciliation remain unconfigured. Synthetic fixtures only; no real source or evidence content, training, clinical use or Production authority.

## Verification

- Successful stored-package registration and review append.
- At-rest tampering blocks a second append and leaves revision/event history unchanged.
- Keep this PR Draft/Open/Unmerged. CI is not formal QA or human approval.
