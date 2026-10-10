# TRN-TECH-025 — Unverified Conflict Handoff Packet Foundation

Date 2026-10-10. Contract state: synthetic-only, internal/test-only, NOT a real reviewer assignment or independently verified case. Business authority for who can triage or legally decide conflict-of-interest remains undefined. No real identity provider, physician regulator, reviewer rights, service-provider contract or conflict severity/recusal policy has been selected.

## Purpose and boundary

Sprint 15 stored *potential relationship disclosures* pinned to an unverified human review note. Sprint 16 can **prepare** a sealed, version-specific packet of those disclosures to support a future human triage. Preparation does not notify a reviewer, appoint one, issue a ruling, clear an actual conflict, reject a person, unlock clinical access, activate a physician or constitute evidence that any relationship exists.

The test-only Npgsql `PgUnverifiedConflictPacketJournal` takes one center, subject, subject kind, precise original accepted evidence revision, human review note request ID, **expected conflict-disclosure revision**, caller-asserted synthetic initiator ref, and unique request UUID. References must be strict `test:` tokens. A full `PgReviewerConflictDisclosureJournal.ReadAsync` replays and validates the paired accepted conflict events and audit history before attempting preparation. **No disclosure history = no potential relationship asserted = do not prepare**, and still does NOT mean conflict-free.

Under `SELECT ... FOR UPDATE` on the scoped disclosure head, a packet can only commit when `current_revision` still equals both the validated prior snapshot and the caller-pinned expected revision. A racing conflict report invalidates a stale preparation. Unique scope+revision and globally unique request UUID prevent duplicate packets and cross-case request replay. Retried identical commands are idempotent; a changed payload with the same request UUID conflicts. A packet for a new disclosure revision is a separate immutable revision-specific record; earlier records remain historical.

The packet stores `command_json`, the captured ordered **unverified disclosure reference manifest**, digest and metadata in `internal_test_conflict_packet`, with `internal_test_conflict_packet_audit` in the SAME transaction. A failure writing its paired audit must rollback the packet entirely. Direct SQL UPDATE/DELETE on accepted packets and audit are rejected by ordinary append-only triggers. Reads verify the command scope, matching event/audit identity, SHA-256 digest, ordered disclosure revision and synthetic refs before returning. SHA-256 without external anchoring is only a local consistency check; database superusers/schema owners can circumvent these safeguards.

## Explicit nonclaims

A prepared packet is neither delivered nor received, and cannot be called **assigned, acknowledged, reviewed, decided, accepted, cleared, verified, authorized, approved or active**. There is NO production outbox/dispatcher, task recipient, notification, adjudicator, human signer, source-backed reviewer role, reviewer authentication, COI verdict or recusal policy. Existing reviewer-source gate is test-only/deny-by-default, and the Sprint 08 clinical-data gate remains closed. No real personal or medical records are stored; all fields are synthetic `test:` references.

New genuine PostgreSQL integration tests cover empty source, accepted note packet/audit, exact and conflicting retries, same-revision uniqueness, stale source revision, historical manifest, concurrent calls, cross-center request replay, SQL immutability, synthetic identifier rejection and audit rollback. Preserve 136 prior review/evidence PostgreSQL tests and the 12 earlier independent .NET/PostgreSQL/Chromium CI jobs. Stage, formal QA, Release, Production, independent Code Review and real reviewer/credential provenance remain blocked without explicit approval. No PR is merged.

## Known limitations for future independent review

- The initiator is caller-asserted synthetic, not authenticated; a test-only caller can prepare a packet without a real authorization source. **Do not expose this journal to a live API, center UI or reviewer**.
- Packet preparation verifies the source revision under the same row lock and captures previously replayed records, but full legal provenance and external tamper-proof signatures are absent. No source-cited professional license/contract can be inferred.
- Test request uniqueness is global. Audit on denied attempts is not complete. Data retention/expiry, multi-replica outbox delivery, recipient, workload queues, privacy access and operational performance have no approved contract; do not invent them.
