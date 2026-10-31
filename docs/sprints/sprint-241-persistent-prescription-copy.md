# Sprint 241 — Persistent synthetic prescription-copy journal

## Scope

Add a PostgreSQL-backed, append-only journal for opaque prescription copies, exposed only in the explicit CI ephemeral environment. The API stores source-provided bytes and version metadata; it does not create or interpret prescriptions.

## Delivered

- Separate test-only copy, event, and audit tables with append-only event/audit triggers.
- Restricted PostgreSQL writer and reader roles; the schema owner remains outside the HTTP adapter.
- Exact request idempotency, optimistic revision checks, member/prescription scope enforcement, replay verification and audit-chain rebuild.
- History capped at 16 versions and 16 MiB of stored document bytes per prescription, with each document capped at 4 MiB.
- Loopback HTTP tests exercise persistence, replay, conflict, stale revision, scope isolation, and explicit authority denials.

## Gates

The routes and DDL initialize only when `TARANOM_PRESCRIPTION_COPY_QA=CI_EPHEMERAL_ONLY` is set by the CI workflow. References must begin with `test:`. No real patient data, source issuer integration, authentication, encryption/key custody, consent, clinical validation, payment, dispensing, national release, or production database is enabled. A stored copy and its hashes do not prove authorship, rights, validity, or clinical safety.

This sprint does not add financial settlement or decide insurer, provider, Taranom, or supporter allocation rules.
