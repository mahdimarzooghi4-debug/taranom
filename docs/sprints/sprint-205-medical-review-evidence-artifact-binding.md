# Sprint 205 — Bind review evidence bytes to recommendation events

**State:** Draft code candidate stacked after Sprint 204. No merge, Stage, formal QA, Release or Production.

## Scope

- Add a review submission type that does not accept an evidence digest claim.
- Store caller-provided synthetic evidence bytes in the content-addressed store and compute their digest.
- Include that digest in the append-only review event, verifying source and evidence blobs before the PostgreSQL event/audit/head transaction.
- Verify that a later at-rest evidence mutation blocks another append without changing the existing journal revision.

## Limits

Evidence bytes and references remain unverified review material. The digest proves byte correspondence only; it does not prove authenticity, custody, source rights, scientific quality, taxonomy correctness, reviewer qualification or acceptance. Synthetic fixtures only. No OCR, interpretation, rights decision, training, clinical use or Production authority is introduced. Filesystem and PostgreSQL transactions remain separate; an evidence blob can be left unreferenced if the later append fails.
