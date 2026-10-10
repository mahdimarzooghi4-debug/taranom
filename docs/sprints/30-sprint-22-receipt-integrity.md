# TRN-SP-22 — Fail-closed integrity of existing synthetic packet receipts

Date: 2026-10-10. Parent Sprint 21 branch `sprint-21-dual-check-synthetic-receipt-history`. Target Draft/Open PR.

Business TRN-BIZ-043/044/053/054 → TRN-TECH-031 → Scrum backlog → Sprint 22 → Code → PostgreSQL tests → CI → preliminary source-level Code Review.

- **BL-220**: prevent bare row-existence checks from masking corruption for different requests targeting a packet with an accepted synthetic receipt.
- **BL-221**: reuse the already existing paired-audit, request/command, issuer-revision and SHA-256 read validation; do not create a second ledger, trusted issuer, or new privilege.
- **BL-222**: cover five deliberately corrupted synthetic PostgreSQL fixture types under transaction-local privileged bypass: audit mismatch, receipt digest, issuer revision, malformed command JSON, missing paired audit.
- **BL-223**: verify exact historical replay, read-only history inspection, request collision from another packet and concurrent duplicate-packet attempts are fail closed, without any extra accepted receipt/audit.
- **BL-224**: preserve 12 inherited CI jobs and record exact HEAD plus preliminary Code Review; independent medical, security and legal reviews remain outstanding.

Excluded: activation, real authority/recipient rights, actual revocation, official professional licenses, consent, payment, real medical records, operational API/UI, Stage, QA Gate, Release or Production. All `test:` responses remain untrusted.
