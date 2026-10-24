# TRN-TECH-134 — Sprint 125 synthetic organization draft activity journal

**Status:** internal PostgreSQL/.NET technical workflow on Sprint 124 HEAD only. The two existing ephemeral SUPPORT/CORPORATE database principals are NOT real organization staff identities or mandates. No HTTP route or external integration.

## Authorized capability (TRN-EP-01/02/12)

After the existing audited CREATE of an opaque organization draft, an internal organization database role may append an opaque \`test:\` **technical activity reference** using \`(item_ref,request_ref,note_ref,expected_previous_no)\`. This enables a durable, versioned activity log for an organizational work item without introducing business adjudication, assignments, diagnoses, financing, consent, membership, clinical or status transitions.

- **Scope and owner:** the PostgreSQL \`session_user\` login and fixed binding decide scope; no scope parameter, token, header or caller impersonation.
- **CAS:** \`expected_previous_no\` is a mandatory exact optimistic concurrency condition (first event uses 0). A fresh request on a stale number returns \`StaleRevision\`, never writes.
- **Retry:** \`(scope,item,request_ref)\` is a durable idempotency key; identical expected number and note ref returns \`Replayed\`, a differing payload gives \`ConflictingReplay\`. Retry is checked before CAS so a later revision does not erase an accepted retry.
- **Atomicity:** a SECURITY DEFINER PostgreSQL function validates login, item, original immutable CREATE audit, and the request; serializes the specific scoped item with an advisory transaction lock; computes the next ordinal and chained SHA-256; inserts once. It is the only runtime write interface. Organizational DB roles have SELECT RLS and EXECUTE, not INSERT/UPDATE/DELETE/TRUNCATE.
- **Lineage:** every activity includes \`activity_no\`, \`expected_previous_no\`, \`request_ref\`, \`note_ref\`, previous hash (GENESIS for the first), and SHA-256 of the documented \`TRN-TECH-134|scope|item|no|request|note|previous\` preimage. Reads reconstruct and validate the entire contiguous chain in a REPEATABLE READ/READ ONLY PostgreSQL snapshot. Hashing is NOT a signature or proof of human identity.
- **Immutability:** PostgreSQL UPDATE/DELETE/TRUNCATE guard triggers protect the log even from ordinary schema-owner DML; schema-superuser DDL remains out of scope. No migration of real databases and no authoritative enrollment.

## Mandatory evidence

Real PostgreSQL probe: two separate organization logins, same item/request keys independently scoped; initial note and subsequent append, identical replay and conflicting retry; stale CAS and concurrent identical retry; scope-isolated reads; direct SQL INSERT/UPDATE/DELETE/TRUNCATE rejection; schema-owner row edit rejection; source audit tamper failure/recovery; no acceptance of non-\`test:\` input. Inherited 14-job full regression must remain green.

## Boundaries and further gates

This is a technical activity ledger for non-personal **test metadata only**. It does not prove an actual operator, legally registered organization, delegated grant, or evidence rights. No real member data, medical interpretation, insurance, credit, payment or business approval. External APIs remain deferred. Activation requires independently verified IdP/org permissions/delegation and formal review; do not claim Stage, QA Gate, Release Approval or Production. Nationwide launch remains \`DENIED_BY_DEFAULT\`.
