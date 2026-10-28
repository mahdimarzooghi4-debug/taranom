# TRN-TECH-241 — Persistent synthetic prescription-copy journal

## Contract

A CI-only PostgreSQL adapter persists an opaque, source-provided document copy using a dedicated head row, immutable event rows, and an append-only audit digest. Each event includes the original bytes, metadata, request identifier, expected revision, and digest. Reads reconstruct the history and verify contiguous revisions, event digests, audit linkage, scope, and the current head before returning any copy.

Writer and reader connections are restricted to dedicated test roles. Writes lock the prescription head and advance the revision atomically. Request retries are idempotent only when all fields and bytes match. A conflicting retry or stale revision is rejected. Member and prescription references are synthetic `test:` values only.

## Resource bounds

A document is limited to 4 MiB; a prescription has at most 16 stored versions and 16 MiB cumulative document bytes. The history limit also bounds reconstruction and HTTP response size.

## Activation and limitations

DDL and routes are initialized only under `TARANOM_PRESCRIPTION_COPY_QA=CI_EPHEMERAL_ONLY`, set only for the local-loopback CI host. The implementation is not an authenticated member service and does not validate an issuer, legal rights, consent, prescription integrity, medical appropriateness, dispensing eligibility, reimbursement, or payment. It is not enabled in Stage or Production and must not receive real health data.

Real use still requires authorized source contracts and integration, identity and access control, privacy and retention decisions, encryption/key custody, issuer authenticity, clinical/legal review, financial allocation and accounting policy, and separate product/operational approvals.
