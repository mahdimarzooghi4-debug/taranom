# TRN-TECH-128 — Sprint 119 support journal PostgreSQL read/write principal isolation

**State:** Technical implementation in isolated test-only localhost workflow; no Production identity or external API.

## Contract and implementation
The existing TRN-TECH-075 support command/read and TRN-TECH-084 history HTTP routes, with the same append-only PostgreSQL test journal, are preserved. The synthetic ASP.NET host is restricted to `CI_EPHEMERAL_ONLY`, loopback 4320, and test-only `taranom_sprint64_center` database.

Separate short-lived PostgreSQL login roles are minted for each CI run:
- `taranom_sprint101_support_test`: existing restricted journal writer, which can SELECT the verified journal, INSERT events and paired audit and update only the case revision, with no DDL/DELETE or other-context access.
- `taranom_sprint119_support_reader_test`: a different **NOINHERIT** principal with **SELECT-only** rights on support case, event and paired audit tables; no INSERT/UPDATE/DELETE on any support table, no schema ownership or login as writer. Its credential is independently generated at CI execution and never committed to the repository.

SupportHttp GET and both SupportHistoryHttp GETs instantiate the *reader* datasource. The support command POST continues to use the *writer* datasource. Existing domain replay and append-only verification are unchanged. Fail startup closed when reader secret or database privileges are missing. The reader never receives schema-owner or writer credentials.

## Evidence and negative tests
Run PostgreSQL privilege introspection and real rejected SQL attempts from the read-only login (INSERT/UPDATE/DELETE, DDL, cross-context SELECT, SET ROLE writer). Verify read-only SELECT still succeeds, while source-backed HTTP writes, reads, versioned history, MVCC and append-only audit work. Full 14-job CI is required, not only a unit test.

## Deliberate limits
This separates **technical DB permissions**, not human identities. It does not establish a session, actor/tenant authorization, confidential member/clinical access, legal complaint, insurer action, financial approval or operational production role. Nothing converts `test:` actor claims to real authority. Production/release remains `DENIED_BY_DEFAULT`. No external APIs, Stage, QA Gate, Release Approval or Production.
