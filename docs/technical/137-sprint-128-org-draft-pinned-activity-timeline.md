# TRN-TECH-137 — Sprint 128 pinned organization draft activity timeline

**Contract:** an internal, strictly synthetic-only technical timeline on the existing TRN-TECH-132–136 PostgreSQL record. This is not a real organization service, patient record, delegated identity or approval mechanism. Scope remains determined ONLY by `session_user` of an ephemeral PostgreSQL principal.

## Product capability

The Persian RTL technical organization draft workspace allows the operator to inspect *historical prefix as-of version N*, browse chronologically in pages of up to five activity references and manually continue to next page. Each page request supplies an exact `expectedCurrentNo` (pinned to a verified existing activity snapshot), `asOfNo` (historical cutoff), `afterNo` (cursor) and `limit` (1…5). Inputs must be canonical decimal ASCII nonnegative integers without leading zeros; `0 <= afterNo <= asOfNo <= expectedCurrentNo`. The source is one `REPEATABLE READ` + `READ ONLY` PostgreSQL transaction which reattests the draft CREATE audit and *all* chained SHA-256 activities before emitting even one event; never rely on the UI to validate SQL integrity. The response contains `itemRef`, `currentNo`, `expectedCurrentNo`, `asOfNo`, `afterNo`, `limit`, `hasMore`, `nextAfterNo` and the bounded event rows.

A concurrent append changing currentNo returns `409 StaleRevision` without any event history. An unrecognized or cross-tenant draft returns 404, malformed request 400. Neither stale/corrupt/forged response nor vanished PostgreSQL may be interpreted as successful history. Other HTTP routes and full legacy technical history remain for compatibility; this new capability does not create business status, assignment, approval or causality.

## QA and boundaries

- Real two-role PostgreSQL probe covers empty prefix, end cursor, pagination, historical as-of, exact revision pin and stale after append, mismatched original audit, trusted hash verification and tenant isolation.
- Real localhost .NET/Python HTTP and Chromium React tests prove page navigation, as-of cutoff, invalid/forged/stale denial. The full inherited 14-job CI is mandatory.
- Strict QA runtime opt-in only with existing `TARANOM_CENTER_DEMO_TEST_ONLY=CI_EPHEMERAL_ONLY` and `TARANOM_ORG_DRAFT_QA=CI_EPHEMERAL_ONLY`; no Production route, no actual member or user identity, external integration, clinical/insurance/finance entitlement or national rollout. Release `DENIED_BY_DEFAULT`. Independent human Code Review and all downstream gates remain blocked.
