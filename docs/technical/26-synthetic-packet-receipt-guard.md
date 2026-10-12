# TRN-TECH-026 — Deny-by-default conflict packet receipt boundary (synthetic-only)

**Date:** 2026-10-10. **Status:** implemented exclusively as an internal non-deployable PostgreSQL test foundation. Business foundations TRN-BIZ-043/044/053/054 and preceding TRN-TECH-018–025. No real licensed reviewer, legal assignment authority, recipient source, clinician regulator or provider contract has been approved or connected.

## Purpose, words and limits

Sprint16 prepared a *local, unverified, immutable packet* of potential relationship disclosures. Sprint17 demonstrates a technical **receipt of a synthetic packet reference** with a missing-by-default authority dependency, not a live human acknowledgement. No person received the packet and no packet was delivered, assigned, accepted, adjudicated or cleared. It must NOT be named “human receipt completed” in any operational system, UI, audit export or Business policy.

`ConflictPacketReceiptGuard` validates exact test-only refs (packet and request UUIDs, center, claimed recipient, versioned synthetic delegation, evidence pointer). It requires an optional, currently **unimplemented** `ITechnicalReceiptAuthoritySource`; the default does not read packet information and reports `AuthoritySourceUnavailable`. The sole permitted source in tests is a local **test double** returning independently bound packet ID, scoped center, same simulated session/recipient identity, current grant version, simulated delegation, nonrevocation and audit flags. Missing, failing, revoked, stale, mismatched or impersonated values fail closed. A fully-positive test double returns only `SimulatedPrerequisitesOnly`, *never legitimate receiving permission*.

`PgSyntheticPacketReceiptJournal` implements atomic synthetic-only recordkeeping **after** that preflight:
- rehydrates and integrity-checks the exact packet, command and paired audit; rejects a claimed recipient equal to the reviewed subject or packet initiator as a minimum negative self-receipt guard (not an approved complete COI policy);
- checks exact current conflict disclosure history via the prior validated journal; a newer reported conflict **supersedes a not-yet-recorded receipt** for the older packet;
- holds a global per-request PostgreSQL transactional advisory lock, an exact packet-row lock, and an exact source disclosure-head row lock to prevent duplicate/competing accepted writes; validates the source revision again under its own lock;
- stores one append-only `internal_test_packet_receipt` per immutable packet, with globally unique request UUID and **paired** `internal_test_packet_receipt_audit` in one SQL transaction; source and stored request/ref/digest/audit are checked on read; a failed paired audit rolls back the entire receipt;
- exactly repeated historical writes return `ExactReplay` (even if a later notice supersedes the old packet), while changed request reuse conflicts and an additional request to the same packet returns `AlreadyNotedForPacket`.

**Important:** This receipt is a controlled **synthetic test observation only**. It does not imply that a real person, recipient registry, trusted IdP, clinical reviewer or regulatory licensing body verified anything. No signed or independently attested acknowledgement, human signature, reviewer assignment/acceptance, electronic delivery, legal case ownership, approved review outcome, clinician activation, clinical-data read, wallet or insurance workflow is included.

## Security and operational caveats

Everything is `test:` refs and ephemeral credentials. The simulated source is forgeable. SHA-256 is a consistency digest rather than an issuer signature and append-only ordinary SQL triggers are not resistant to database owners/superusers. Global advisory locking and in-memory full-history replay are unbenchmarked for production load. Absent actual authenticated recipient source, privacy/right-to-know rules, signed package and reviewer authorization, the test-only receipt path must never be connected to HTTP, the center panel, medical files or a production database.

The preceding Sprint08 protected-data interlock remains DENY-ONLY. Neither simulated source nor technical receipt can change its state.

## Acceptance and gates

15 new genuine PostgreSQL integration tests include missing/outage authority, fake-positive yet nonauthorizing receipt, wrong grant/session/scope, subject/initiator self-receipt denial, stale packet revision, exact/conflicting replay, concurrent distinct/identical requests, append-only audit, and audit-failure rollback. Preserve **all 150** inherited reviewer/evidence PostgreSQL tests and **12** CI jobs, including .NET, real PostgreSQL and Member/Center Chromium regression suites.

Real reviewer identity/delegation, trustworthy review source, independent professional/licensing proof, provider contract registry, legal COI/recusal and receipt semantics, document provenance, privacy/retention, production SQL/RLS, and independent medical/security/privacy Code Review remain unresolved. No Ready/Merge/Stage/formal QA/Release/Production without explicit authorization.
