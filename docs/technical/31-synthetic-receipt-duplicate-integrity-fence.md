# TRN-TECH-031 — Integrity-fenced duplicate synthetic receipt detection

**Date:** 2026-10-10. **Scope:** strictly isolated PostgreSQL and `test:` claims; not a production admission, identity authority, medical decision, clinician certification or human receipt. Parent TRN-TECH-030 / Sprint 21 Draft PR #28. Business TRN-BIZ-043/044/053/054 remain unchanged.

## Observed security inconsistency

A same-request retry already reads and verifies the existing synthetic receipt together with its paired audit and versioned SHA-256 digest before returning a historical outcome. A **different request for a packet with an existing receipt** previously returned `AlreadyNotedForPacket` based solely on `SELECT packet_id`. This could mask a corrupted historical row, inconsistent issuer revision or missing/mismatched paired audit. Although no operational authorization arises, success-shaped historical conclusions should not be produced over known damaged evidence.

## Narrow technical contract

1. Maintain first and under-lock second TEST DOUBLE checks, PostgreSQL packet/request locking and all existing synthetically nonauthorizing outcomes.
2. In the existing new-request path, AFTER checking current source revision and positive simulated issuer consistency, use `ReadLockedAsync` rather than a bare existence query for duplicate packet detection. This shared reader checks command JSON against packet/request/center/recipient, version-2 domain-separated SHA-256, syntactic `test:` issuer with positive revision and the paired audit digest/request. A valid accepted prior receipt yields `AlreadyNotedForPacket`, exactly as before.
3. Missing paired audit, malformed JSON, mismatched issuer revision, broken receipt digest or altered audit digest fail closed with existing `InvalidOperationException("TEST_PACKET_RECEIPT_CORRUPT")`, not a duplicate success label. Do not create a second receipt or audit, repair history, mutate an original row, or create a new authorization result.
4. Preserve priority for wrong-request collision, exact replay, changed-command replay and simulated source outages; preserve atomic insert + audit and all prior concurrency protections.
5. Add genuine PostgreSQL negative tests using **isolated privileged test-fixture mutation** with transaction-local `SET LOCAL session_replication_role = replica`. The bypass is ONLY for adversarial tests, never a product or repair capability. Cases include 5 ways to corrupt stored data, same-request replay/read/history rejection, packet duplicate with a new request, request reuse across distinct packets and concurrent new requests for damaged history.
6. Maintain CI as the inherited independent twelve jobs, including Member and Center Chromium, DENY-ONLY clinical data interlock and all pre-existing isolated PostgreSQL suites. No HTTP/API/BFF/UI or external adapter changes.

## Limitations and pending external decisions

The tests detect straightforward inconsistency, **not arbitrary privileged DBA tampering**. Privileged operators can manipulate records, disable triggers and recompute an unkeyed digest to forge consistent history. PostgreSQL transaction/row locks cannot secure a hypothetical outside registry and the two TEST DOUBLE issuer reads cannot guarantee instantaneous revocation or detect ABA. No real identity, reviewer rights, signed issuer, authorized professional credentials, patient consent, current delivery permission, actual clinical data, bank/insurance/payment, doctor activation or Production deployment is introduced. An authoritative issuer, revocation semantics, reviewer appointment/authority, legal privacy rights and independent medical/security/legal sign-off remain blocked on formal contracts and external integration.

## Acceptance

Confirm isolated PostgreSQL reproduces the five corruptions and shows failure, audit/receipt row counts do not increase, valid duplicate packet still returns historical `AlreadyNotedForPacket`, original same-request behavior is retained, and request collision/concurrent cases fail closed. Record exact-SHA CI result and source-level code review. No Ready/Merge/Stage/QA Gate/Release/Production without explicit approval.
