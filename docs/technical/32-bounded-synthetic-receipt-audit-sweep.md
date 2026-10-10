# TRN-TECH-032 — Read-only bounded synthetic receipt integrity sweep

**2026-10-10; scope:** test-only internal Npgsql data over `test:` identifiers, stacked on Sprint22 Draft PR #29. No genuine issuer/credential, legal receipt or clinical access.

## Observed gap and contract

An individually identified synthetic receipt can already be verified against its immutable command, issuer/version, v2 SHA-256 digest and paired audit. But no read-only bounded integrity scan exists to detect corrupted historical pairs without a caller knowing each packet ID.

Introduce `PgSyntheticReceiptAuditSweep` over the SAME verifier (`PgSyntheticPacketReceiptJournal.ReadLockedAsync`, made internal), not an alternate hash/authorization service. Each call accepts a strict synthetic center scope, optional nonempty UUID cursor and page size 1..100. The upper bound is an engineering resource cap, not a business/medical threshold. Use keyset `packet_id > cursor`, sorted UUIDs, fetch pageSize+1, and `REPEATABLE READ` plus `READ ONLY` per-page PostgreSQL transaction. Count any receipt/paired-audit corruption as inconsistent. SQL, permission or connection errors propagate; never report false success.

Return only `Inspected`, `Inconsistent`, `HasMore` and a synthetic packet UUID cursor. No raw evidence, medical data, permission conclusion, compliance badge or real source status. Each page is a separate DB snapshot, **not** a single atomic scan across pages or with outside registries.

Use a dedicated **CI-only** `taranom_sprint23_receipt_auditor` PostgreSQL role with SELECT on the two receipt/audit tables only; it cannot modify them or access unrelated review/evidence tables. This is no production credential.

## Required tests and limitations

Real isolated PostgreSQL tests verify scoped reading, multiple pages, five controlled corruption fixtures, no mutations, SQL permission denial, negative scope/cursor/limit. Preserve inherited 12 CI jobs including actual Chromium and protected-data DENY-ONLY tests.

The sweep cannot detect consistent forged receipt/audit pairs, privileged removal of both rows, a row hidden by altered center scope, external issuer lie/ABA or loss of data outside the scan; ordinary SHA-256 is not a signature or privileged-owner tamper resistance. No trusted IdP, reviewer authority, clinical record, finance or API/UI is introduced. Independent medical/security/legal reviews and all external source/revocation/consent contracts remain open. No Ready/Merge/Stage/QA Gate/Release/Production.
