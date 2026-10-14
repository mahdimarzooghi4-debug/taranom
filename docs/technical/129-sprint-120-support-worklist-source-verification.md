# TRN-TECH-129 — Sprint 120 internal support worklist

**Status:** isolated source-backed development/test only. No external API, IdP, patient data, provider, insurer, clinical authority or payment. This is an internal operator *demo worklist*, NOT a real operational directory.

## Product increment
Reuse the **existing** TRN-TECH-075 PostgreSQL append-only support journal and TRN-TECH-128 separate reader-only DB login. The `/internal/demo/v1/support-worklist` GET enumerates only committed `test:` case references with revision and follow-up count, sorted by binary/canonical case reference. The operator may select one in the existing React support workspace and explicitly read its versioned case and follow-up history.

## Invariants and gates
- Host requires `CI_EPHEMERAL_ONLY`, loopback port 4320 and isolated `taranom_sprint64_center` database. No Production route or change to member/center portal authentication.
- **Read-only** under the existing SELECT-only `taranom_sprint119_support_reader_test` login; no new table, duplicate ledger or mutable list cache.
- Query accepts **exactly** `afterCaseRef` and `limit` (1–10, canonical integer), with empty first-page cursor or valid `test:` cursor only. Keyset pagination uses the same C binary collation for ordering and comparison; bounded LIMIT+1.
- Every selected row, **including the lookahead row**, is rehydrated and independently validated against canonical immutable event+paired-audit records **within one PostgreSQL REPEATABLE READ / READ ONLY snapshot**. Corrupt/missing history or privilege failure rejects the entire page (503), never silently skips records.
- Response contains only `caseRef`, `revision` and `followUpCount`; no member, actor, note or evidence references, and explicit `memberAuthenticated=false`, `nationalReleaseAllowed=false` and other denial flags. Browser verifies schema, flags, strict increasing IDs, revision and next cursor, and fails closed, clearing the prior list.
- Selection only changes the test reference in the existing UI. It does not grant access or automatically write; while a support POST is uncertain, selection is disabled.

## Acceptance evidence
Mandatory Python HTTP full-stack test uses actual .NET and PostgreSQL to verify two recorded cases, ordered 1-row pages, follow-up counts, absent sensitive source fields, denied malformed query and hostile Host. Playwright Chromium tests verify actual DB-backed selection, hostile/falsified response denial and invalid cursor without network traffic. Existing support writes, journal replay, history, MVCC reader/writer isolation, backup/restore and entire full regression must remain green.

## Explicit limitations / next contract
Real multi-tenant/center operator authorization, authenticated server sessions, per-resource grants, legal support submission and private work queue access are **not implemented**. An anonymous enumerating endpoint is confined to the ephemeral synthetic demo and **must not be promoted or reused as a Production endpoint**. The real operational list requires separately approved, independently authenticated/authorized server scope contracts and actual lawful data rights, including revocation and audit. No Stage, QA, Release Approval, Production or national launch. Release stays `DENIED_BY_DEFAULT`.
