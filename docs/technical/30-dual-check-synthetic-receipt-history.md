# TRN-TECH-030 — Dual synthetic issuer check for replay and history inspection

**Date:** 2026-10-10. **Status:** internal non-deployable test contract, built on Sprint 20 TRN-TECH-029 and existing TRN-BIZ-043/044/053/054. The issuer and all identity/grant fields remain `test:` Test Double claims. No real human access or legal authority.

## Observed test-only gap

New receipts already inspect a synthetic request-bound issuer twice, once before reading an unverified packet and once after database locking. Sprint 20 added comparison of the single preflight issuer to the immutable receipt on exact replay; the separate history inspector also compared its single initial issuer check with the read-back database history. Both historical paths could return a nonauthorizing positive historical match after their first TEST DOUBLE answer had changed or disappeared during the intervening SQL operations.

## Contract

1. Preserve the original fail-closed initial source check BEFORE synthetic packet/history SQL work. Invalid, unavailable, revoked, inconsistent, malformed or wrong-request source responses never produce a positive historical result.
2. For an existing receipt with the same request UUID, continue taking the PostgreSQL per-request transaction advisory lock, packet lock and reading/validating the immutable receipt plus paired audit v2 digest. A **different command** returns `ConflictingReplay` before any extra source call and without modifying history.
3. Only for an **exact command** replay, re-read `ITechnicalReceiptAuthoritySource` while the transaction holds locks. Late outage/revocation/request mismatch fails closed using existing source outcomes. A syntactically valid positive claim whose issuer or revision changes between preflight and late read returns `SimulatedIssuerSnapshotChanged`; it never returns `ExactReplay`.
4. When both simulated issuer identity and revision match each other, still compare to original immutable receipt fields. If the historical row and claimed source disagree, return `HistoricalIssuerVersionDifferentOnReplay`; only exact agreement permits historical `ExactReplay`. Neither means authorization, current entitlement, external recency nor receipt delivered.
5. For `PgSyntheticReceiptHistoryInspector`, retain the original preflight before SQL, read and integrity-check the historical receipt, and return `NoHistoricalReceipt`, `HistoricalReceiptCorrupt` or `RecordedCommandMismatch` as appropriate. Only for an existing exact history row, perform a second independent TEST DOUBLE lookup **after SQL**. Late missing/revoked/invalid responses return `SourceUnavailable` or `RecipientOrGrantUnverified`. Changed issuer or revision between the checks returns `SimulatedIssuerSnapshotChangedDuringInspection`. Source stable but different from stored historical issuer returns `HistoricalIssuerVersionDifferent`; stable matched returns `SimulatedVersionMatchesHistoryOnly`.
6. Both checks are observations; the receipt/audit remain untouched, no new persistence models, schemas, controllers, BFF endpoints or permissions. Exercise these contracts and unchanged predecessor cases in real isolated PostgreSQL CI.

## Non-guarantees

Two calls to a TEST DOUBLE cannot prove authoritative source authenticity, instant revocation, monotonic version history, A→B→A/ABA safety, genuine reviewer assignment or a transaction across PostgreSQL and a hypothetical external registry. The history inspector's second call is *after* its own read transaction ended: it is NOT a database/external consistency lock. The replay check is only under PostgreSQL locks, which have no authority over an external registry. SHA-256 and SQL triggers are not privileged-owner tamper protection or legal signatures.

There is no real IdP, authorized reviewer, provider credential, actual external revision/revocation adapter, legal authorization for health data, doctor activation or real medical/financial integration. The separate protected data path remains **DENY-ONLY**. All professional legal/security/medical review and the source rights model remain unresolved; no Ready, Merge, Stage, QA Gate, Release or Production.

## Required CI evidence

Verify stable historical replay uses 2 synthetic source lookups; test between-check issuer replacement and revision changes, under-lock source outages, late simulated revocation/assignment changes, late request rebind, competing command precedence and preservation of exact immutable receipt+audit. Test history-inspector second source check after database read, changed source, late outage, late revocation, stable result and wrong-command precedence. Preserve inherited 12 jobs, member/center Chromium and PostgreSQL tests. All resulting positive labels remain explicitly nonauthorizing.
