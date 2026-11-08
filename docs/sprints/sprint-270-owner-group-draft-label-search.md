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
