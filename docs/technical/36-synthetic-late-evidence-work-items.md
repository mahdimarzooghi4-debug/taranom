# TRN-TECH-036 — Synthetic late-benefit evidence work item + review-note journal

**Date:** 2026-10-10. **Base:** Sprint26 Draft PR #33. Based on accepted TRN-BIZ-012/013/014/015/016, particularly TRN-BIZ-014: a benefit attributable to a prior valid membership period may be recognized after membership lapses **only after competent entitlement verification and document review**. The authorized reviewer, authentic official membership/source events, exact income entitlement, applicable rights and financial posting contract remain undecided. This sprint DOES NOT implement any of those decisions.

## Purpose and non-goals

Sprint26's `SyntheticMembershipCoverageWindow` classifies purely synthetic *closed* historical time windows and correctly refuses to infer entitlement. Sprint27 creates a distinct **unverified evidence work item** and separate **unverified claimed human review note**, so evidence lineage can be captured without using the temporal result as a financial decision. There is no automatic coverage-to-credit path, no review approval, no member activation, no money, and no medical data or outside source.

## Domain contract

- Introduce `LateEvidenceWorkItem` in the non-packable MemberGovernance assembly. It is an internal pure append-only reducer with exactly two actions: `SubmitUnverifiedEvidence` and `RecordUnverifiedHumanNote`. The latter is **not** a PASS/FAIL/APPROVE/REJECT; it only records a `test:` reviewer-claim marker and a `test:` opaque note-reference.
- The submission pins a synthetic case, synthetic member, half-open attribution interval `[start,end)`, `test:` opaque evidence reference, unverified submitter-claim reference, UUID request ID and expected revision. No financial amount, member KYC, source income, identity number, bank account or legal medical permission may be stored or inferred.
- A note pins the **same exact** case/member/period/evidence/submitter and the original submission request UUID, with a different (merely claimed) reviewer marker and separate opaque note reference. The synthetic marker difference does not establish independent real reviewer identity; a real authority contract is still needed.
- Each accepted command advances a monotonically increasing case revision; exact replay is safe even after later notes, reusing a request UUID with changed command fails, stale expected revision fails, second new submission to a case is rejected, and no submission can be invented by a note. No status can ever confer entitlement, credit, clinical access or authorization.
- The reducer's `RecordedUnverified` result is a technical persistence status, **never financial or membership eligibility**. A case can receive multiple unverified notes; there is no final business decision or human promotion command in this sprint.

## Isolated PostgreSQL journal

- Add `PgLateEvidenceWorkItemJournal` in the existing nondeployable ProofingJournal.Postgres test assembly, **not** a parallel Production ledger or live finance API. Tables are prefix `internal_test_late_evidence_*`, isolated to ephemeral CI DB: case head + events + paired audit.
- Runtime test database role has SELECT/INSERT/UPDATE only on case heads and SELECT/INSERT on event/audit; cannot edit accepted event/audit. Ordinary DML by table owner also blocked with append-only triggers. Case row lock and transaction bind accepted event, audit and revision CAS atomically, supporting competing simulated instances. Audit-insert failure rolls back **everything**.
- Before replay, rehydrate complete accepted history through exact reducer and cross-verify case revision, request IDs, event action, serialized payload SHA-256 and paired audit. Corrupt history throws `TEST_LATE_EVIDENCE_JOURNAL_CORRUPT` even for an otherwise identical replay. Digest is internal integrity checking, **not** a trusted signature or protection against privileged DBA rewrite/deletion.
- Invalid commands and business-precondition denials do not insert accepted case rows, notes or financial data. Unlike a real regulator-grade audit, there is **no separate persisted denial register** yet; never claim a full external audit or operational review provenance. The only caller identities are untrusted `test:` markers. No direct use of `SyntheticMembershipCoverageWindow` is made to infer entitlement.

## Acceptance tests

Pure xUnit checks: submission/note events, idempotent replay after newer events, conflicting key, stale CAS, scope/period/evidence/submitter/original-request binding, missing original, another claimed reviewer, missing and real-source fields, and absence of financial/positive-permission vocabulary. Real PostgreSQL 18 integration cases: durable append+audit, replay, competing requests, rejected forged note, database least privilege, simulated audit insertion failure rollback, privileged controlled digest-tamper fail-closed, no real identifiers. Preserve inherited twelve independent CI jobs, Member and Center built Chromium, protection data DENY-ONLY and all prior synthetic clinical review tests.

## Deployment / decisions

Required external authorizations remain open: real member and identity provider, authoritative period versions/corrections, actual evidence ownership/source rights and storage, legally appointed reviewer and independence/conflict criteria, approval/rejection/governance definitions, income/source/custodian accounting, duplicate attribution prevention, wallet posting/reversal and treatment-network contracts, privacy/legal/security review. Nothing here can take effect as an operational service. No Ready for Review, Merge, Stage, QA Gate, Release or Production.
