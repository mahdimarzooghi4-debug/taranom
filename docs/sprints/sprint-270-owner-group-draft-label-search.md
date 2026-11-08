# Sprint 270 — Private owner group-draft label search

Status: DRAFT; stacked on PR #295. Contract: TRN-TECH-292, derived from existing planning draft authorization.

## Goal
Find an older owner-created invitation planning draft by its private label without manually paging every draft or disclosing other accounts.

## Steps
1. Extend the existing authenticated PostgreSQL draft GET with parameterized literal substring q filtering **before** limit and with strict q validation and exact-reconciliation isolation.
2. Add real PostgreSQL tests for buried matches, owner privacy, 20+1 cursor, literal wildcard handling and invalid parameters.
3. Extend the connected owner Figma-derived list and typed client with explicit submit/clear, preserved search across pagination/refresh, no automatic search and invalid/denied cleanup.
4. Cover real Chromium and run exact-head 2/2 +14/14 CI; record COMMENTED review.

No new lifecycle, recipient verification, payment, paid membership, guardian authority, clinical/health record access, communication channel, external integration, Stage or Production. PR remains Draft/Open/Unmerged; independent human approval and owner instruction still required for merge. National release DENIED_BY_DEFAULT.

## Exact-head integration checkpoint (not a release claim)

- Implemented candidate SHA before this evidence-only commit: `162d482e87bc36864fa9cb829129f4036d97e09f`.
- [Figma Appointment Integration #42504761950](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/42504761950): **SUCCESS 2/2**, including real PostgreSQL HTTP search/privacy/pagination tests and real Chromium search/clear/navigation cases.
- This record changes the exact SHA; both Figma Integration and canonical 14-job mainline regression must be rerun and pass on the resulting exact HEAD before Code Review.
- PR #297 is temporarily retargeted to `main` solely to invoke the canonical mainline regression, then restored to its actual stacked base PR #295. No merge, Stage, QA Gate, Release or Production.

### Mainline regression dispatch checkpoint

The exact-head canonical 14-job regression requires a commit on this temporarily main-targeted Draft PR. This note is a non-runtime change to trigger both required workflows; their results must be verified for the resulting SHA, not inherited from any prior commit. Restore the stacked PR base only after success, and keep national release DENIED_BY_DEFAULT.
