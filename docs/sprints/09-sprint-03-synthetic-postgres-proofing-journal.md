# TRN-SP-03 — Isolated PostgreSQL Proofing Journal Foundation

Date: 2026-10-10. Scope: owner-authorized internal Code on Draft PR #9, **synthetic PostgreSQL integration only**, with no real operator or KYC adapter.

## Technical boundaries

- `Taranom.MemberGovernance` pure domain rules are unchanged. A **separate** Npgsql-based project `Taranom.ProofingJournal.Postgres` is internal/friend-test-only and requires an isolated `TARANOM_TEST_PG` database. It is **not** a Production identity or Member API.
- Synthetic-only `test:` case references and commands containing exclusively enums, booleans, revisions and random UUIDs are committed. No raw KYC assertions, national identifiers, consent, medical data, financial figures, user identifiers or session tokens.
- Per-case row lock in PostgreSQL serializes concurrent updates across distinct connection pools. The existing pure reducer determines exact historical replay vs payload conflict, stale case/identity revision and permitted internal state change.
- Accepted transitions commit a current-state projection, immutable event and append-only audit record **atomically**, with a minimal SHA-256 consistency checksum. Rehydration verifies every event against the paired audit and deterministic reducer; inconsistent or truncated history fails closed. SHA-256 is an **integrity checksum**, not a cryptographic signature against a privileged database administrator.
- Runtime test role has SELECT/INSERT on event/audit but not UPDATE/DELETE, while triggers reject modifications to append-only history. The mutable projection is not an independent right to change past decisions; its tampering is detected through replay. Privileged superusers can override these controls; production-grade compliance is **not** claimed.
- The injected `test:abort-` audit failure proves transaction rollback on lost audit writes. Real retry cadence, records retention, a persistent identity/member mapping, privacy policy, migrations, provider integration and actual authorization are **not** introduced.
- Race-safe under the isolated PostgreSQL transaction and isolated test role is **not** a national Production architecture. Real credential management, migration governance, tenant/user isolation, row-level access, backup/recovery, legal policy and independent review remain unimplemented.

## CI and gates

The Sprint 03 workflow must execute a **separate PostgreSQL integration job** with real concurrency, replay, append-only privilege/trigger and audit failure-injection tests, alongside the three existing Sprint 01–03 regression jobs. Success is recorded only for the exact tested head SHA; independent Code Review and external evidence [Issue #6](https://github.com/mahdimarzooghi4-debug/taranom/issues/6) / [Issue #8](https://github.com/mahdimarzooghi4-debug/taranom/issues/8) remain open.

No real login, membership activation, wallet, insurer, healthcare, research/AI or Production integration. No Merge/Stage/QA/Release/Production claim.
