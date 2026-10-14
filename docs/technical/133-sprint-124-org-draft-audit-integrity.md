# TRN-TECH-133 — Sprint 124: atomic organization draft audit and lineage

Status: internal technical fixture only, stacked Draft on Sprint 123. No external API, real actor authentication, actual organization onboarding, clinical/financial record or approval, Stage or Production.

## Concrete owned-domain increment
Use the existing two bounded synthetic PostgreSQL organization scopes, their `session_user`-based FORCE RLS, and the existing `internal_test_org_draft_item` table. No parallel source of draft truth.

- On first accepted `INSERT`, a PostgreSQL **AFTER INSERT SECURITY DEFINER** trigger writes one `DRAFT_RECORDED_TEST_ONLY` event to `internal_test_org_draft_audit`, in the **same transaction** and independently resolving the role-to-scope binding. No separate user-driven audit creation endpoint exists. If audit creation fails, the draft insert fails atomically.
- Event identity is `(scope_ref,item_ref,event_no=1)`. It pins original source, event vocabulary, and deterministic SHA-256 of `TRN-TECH-133|scope|item|source|DRAFT_RECORDED_TEST_ONLY|1`. No event timestamp or made-up human actor. This is not a cryptographic signature or a trust attestation.
- Repeated requests with the same `scope_ref,item_ref,source_ref` are idempotent: they return `Replayed` **without appending a second CREATE event**. A changed source under that key remains `ConflictingReplay`, leaves the original data and audit intact, and does not create an event.
- PostgreSQL audit rows have FORCE RLS with SELECT-only per current DB login. Roles cannot INSERT, UPDATE, DELETE, TRUNCATE or change the audit. Explicit PostgreSQL mutation/truncation guard triggers deny row-level edits **even by the ephemeral schema owner**; schema-owner DDL and superuser privileges are inherently outside this guarantee.
- The bounded .NET service verifies the presence and semantic/hash linkage of the audit row for **Record/Replay, detail read and every listed row including the lookahead**. The audit and corresponding draft are read from the same transaction/snapshot; missing or mismatched history fails closed rather than silently returning unverified data. `ReadAuditAsync` exposes a **test-only, scoped read-only** audit event and cannot read another organization's event.
- The fixture initializer refuses silently adopting existing unjournaled draft rows. No operational database migration is authorized.

## Evidence
Extend the same full 14-job CI PostgreSQL probe with: one CREATE event per accepted draft under 8 concurrent competing requests; no duplicate event on exact replay/conflict; identical item refs in different organization scopes retain separate evidence and hashes; missing cross-scope audit reads; forbidden direct audit DML and TRUNCATE from organization roles, forbidden owner UPDATE/DELETE/TRUNCATE; deliberate owner tampering with draft source produces a hard read/list/replay integrity failure, followed by explicit fixture restoration and successful revalidation. The earlier direct SQL isolation, source-backed web, recovery and release-denial checks remain mandatory.

## Boundaries
Only synthetic `test:` opaque references. No external IDs, organizations, staff, consent, delegated grants, insurer, medical or financial authority, authenticated production HTTP routes or real-member data. Immutable audit is **technical evidence only**, not proof of trusted human action or business approval. No formal QA Gate, merge, release, Stage or Production. National launch remains `DENIED_BY_DEFAULT`.
