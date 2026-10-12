# TRN-TECH-028 — Version-fenced synthetic issuer evidence for packet receipt

Date: 2026-10-10. Status: isolated test-only contract, **NOT** an identity provider, a real authorization, legal receipt, clinic permission, issuer signature, verified professional qualification, reviewer/doctor approval or a real revocation system. Parent Sprint18 PR #25, technical TRN-TECH-027. Business TRN-BIZ-043/044/053/054 unchanged.

## Scope and safety problem

The previous receipt code checked the same simulated request-bound source twice. Both checks could be syntactically positive even if the purported **issuer** or its snapshot **revision** changed between them: the recipient/grant version was unchanged, but the source provenance behind the grant was different. Further, existing read-only `ReadAsync` returns a **historical technical receipt**; consumers must not misinterpret the receipt's existence as a **current** reviewer entitlement.

There is still NO actual trusted identity/reviewer source. This Sprint introduces **pure synthetic markers** for consistent test lineage, not a new decision rule or an assertion of reliability:

- Extend `TechnicalReceiptSourceEvidence` with `SimulatedIssuerRef` (strict `test:` marker) and `SimulatedIssuerRevision` (positive integer, **not a business threshold or policy**). Both are returned by a TEST DOUBLE only. Missing/malformed values fail closed, even if all simulated boolean flags are true.
- The `ConflictPacketReceiptGuard.InspectAsync` returns a typed `TechnicalReceiptSourceCheck` carrying an accepted **simulated** source result only when the full request/packet, issuer, center, simulated session/recipient, grant revision, original receipt evidence ref and test flags match. The previous `CheckAsync` remains a compatibility wrapper.
- In `PgSyntheticPacketReceiptJournal.RecordAsync`, compare `SimulatedIssuerRef` AND `SimulatedIssuerRevision` from the first and second source reads. If either differs, rollback the PostgreSQL transaction and return `SimulatedIssuerSnapshotChanged`. Missing/refused/changed grant or identity remains fail-closed as in Sprint18.
- Store issuer ref and exact issuer revision in the **same accepted synthetic receipt** as request/recipient and paired audit. Upgrade the domain-separated SHA-256 consistency digest to v2 binding `packetId`, original command JSON, simulated issuer ref and revision. Ordinary SQL UPDATE/DELETE remains guarded; accepted read checks fields and audit against the v2 digest and `test:` syntax. A durable historical issuer marker is not independent external attestation; privileged DB owners can still rewrite data.
- Add distinct internal read-only `PgSyntheticReceiptHistoryInspector` which checks the simulated current issuer BEFORE touching SQL. Its only positive result is `SimulatedVersionMatchesHistoryOnly` — **not authorized, no human acknowledgement or current permission**. It returns source unavailable/unverified, missing historical record, mismatch to original command, inconsistent/corrupt receipt and changed issuer/version as separate nonauthorizing results. Historical receipt is preserved if a new issuer revision is later reported; no automatic deletion or legal status change.

## Explicit guarantees and non-guarantees

This foundation detects a synthetic issuer/version mismatch between two reads and distinguishes the historical record from an asserted latest simulated source version. It does **NOT** guarantee monotonic revisions, forbid a malicious TEST DOUBLE returning A→B→A between reads, guarantee actual revocation after the last read, atomically bind an external trusted registry with PostgreSQL, or establish an approval/real data permission. No external IdP, trusted reviewer-appointment/issuer implementation, signed token, verified license source, official contract, BFF/HTTP API, clinical/private file, patient consent, payment, fund or insurance integration is introduced.

The earlier protected-data interlock remains DENY-ONLY and this test-only component must not be exposed to a real center/provider panel. No production algorithm, conflict taxonomy, human assignment, recusal criterion, retention period, endpoint or credentials is invented.

## Tests and governance

Tests in genuine PostgreSQL cover invalid issuer/ref/positive revision markers, issuer identity/revision change before the second source check (no accepted receipt or audit), stable issuer revision accepted only as a synthetic technical receipt, persisted issuer lineage and paired audit, historical inspector denial without source, changed issuer/version/revocation, wrong original request and receipt evidence, missing receipt, and explicit nonauthorizing enum vocabulary. Preserve 174 inherited reviewer/evidence integration tests and the twelve independent CI jobs including two real Chromium suites and all other .NET/PostgreSQL regressions.

Production trust-root protocol, revocation ordering, source-session privilege, reviewer rights, signed assignment, issuance history, legal privacy/consent, operational denial audit and scalability remain **unresolved contractual blockers** requiring explicit authorized decisions and independent security/legal/clinical review. PR remains Draft/Open; no Ready/Merge/Stage/QA/Release/Production.
