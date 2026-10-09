# TRN-SP-01 — Security-oriented Code Review of Synthetic .NET Foundation

- **Review date:** 2026-10-09
- **Type:** assistant-conducted source and CI-evidence review, **NOT independent human Code Review approval**.
- **Reviewed implementation SHA:** `9f002fc2c442f2d0366552cf845ff3cf7f3db67c` (Sprint 01 Draft PR #3, based on Technical Draft PR #2).
- **Actual evidence:** GitHub Actions [PR run 37953367961](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37953367961) and [push run 37953362234](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37953362234), both completed **SUCCESS** for this exact code SHA.
- **Review status:** **SOURCE REVIEW COMPLETED WITH FIXES / INDEPENDENT REVIEW PENDING**. No merge, Stage, QA Gate, Release, Production or real identity/financial/clinical rollout authorized.

## 1. Scope and architecture integrity

The owner authorized only synthetic C#/.NET Identity/Access, independently scoped consent evidence, append-only audit and nonproduction PostgreSQL tests. Source consists of a .NET library (no deployable ASP.NET API/Production adapter), a synthetic smoke-test executable, PostgreSQL schema for tests and GitHub Actions for CI.

Architecture invariants remain: .NET is the operational policy owner; Python AI is **not** implemented or run; no external AI APIs, real IdP, real consent, member medical records, insurance, treatment, financial ledger or payment paths are introduced. **Member-facing Web App** remains mandatory `TRN-EP-13`, with separately authorized future implementation and a candidate React/TypeScript stack.

## 2. Findings, remediation and evidence

| ID | Severity before fix | Finding | Actual remediation | Evidence |
|---|---|---|---|---|
| CR-01 | HIGH | Consent stream/evidence/replay keys omitted `tenant_ref`, so identical member references from distinct tenants could share state. | `tenant_ref` added to stream/evidence/replay composite keys and all SQL reads, locks, writes and lookups; audit includes tenant reference. | Synthetic PostgreSQL tests exercise identical member IDs in different tenants, independent grant/withdrawal and unchanged original tenant state. |
| CR-02 | HIGH | `ExpectError` caught any exception; a database failure or code defect could falsely pass tests meant to assert specific denial. | Replaced with `ExpectDenial`, `ExpectSqlError` and `ExpectSqlState`; expected exact failure category, PostgreSQL SQLSTATE and (for trigger exceptions) expected message. | CI passed tests for version conflicts, idempotency conflicts, invalid purposes, write rejection, rollback. |
| CR-03 | HIGH | All consent/audit test calls used PostgreSQL schema-owner credentials with privileges sufficient to alter constraints/triggers; least-privilege isolation was not proven. | Test schema setup now runs under owner role, application calls use dedicated non-owner login with only granted read/append and stream-version write privileges; test checks runtime cannot UPDATE/DELETE audit/evidence or DROP trigger. | Real PostgreSQL integration tests in both exact-head successful workflows. |
| CR-04 | MEDIUM | Test expectations conflated a stale opposite operation with an identical-operation replay payload conflict; strict assertions surfaced it. | Added separate `VERSION_CONFLICT` and `IDEMPOTENCY_CONFLICT` checks. | First intermediate CI failure investigated and corrected; final exact-head CI green. |

These are **code and test fixes**, not evidence that security has been independently certified or that PostgreSQL administration itself is tamper-proof.

## 3. Reviewed positive invariants

- Access is default-deny; user/tenant match and explicit test action are required. Sponsor role does not inherit private member-data access. Client-supplied untrusted test origin is rejected in the synthetic policy.
- Consent purposes are distinct; consent to care does not imply secondary AI research permission. Missing/stale scope, subject mismatch and unknown purpose fail closed in the synthetic API. A consent withdrawal appends evidence rather than altering history.
- Consent mutation and audit creation occur in one PostgreSQL transaction. Fault-injection test blocks audit insertion and confirms no partial consent state/version change. Concurrent same-version writes allow one winner.
- PostgreSQL triggers reject UPDATE/DELETE of synthetic audit and consent history even when executed by schema owner; runtime role cannot execute restricted modifications or remove audit trigger.
- Replay with identical normalized operation returns prior result without duplicate audit; changed input under the same request reference fails. No real money/medical/insurance side effect exists.
- Synthetic smoke tests run with an ephemeral CI PostgreSQL server and synthetic fixture identifiers; this does **not** substitute for real identity provider verification, production RLS design or legal consent.

## 4. Residual restrictions / review gaps

1. **Independent reviewer still required** before marking Code Review formally accepted. This document is a source/CI review by the implementation assistant; not a second-person approval.
2. **Test-only boundary is not Production security design.** `InternalsVisibleTo` is a limited assembly-access convention, not cryptographic authentication. No real IdP, signed claims, legal consent capture, role/subject governance, Production tenant controls or secure API ingress has been implemented.
3. **Audit coverage is currently mutation-oriented.** Pure access-policy evaluations do not yet produce persistent audit entries for every read/deny; audit read projections, correlation restrictions, retention law and log redaction tests are not complete. Do not declare all `TRN-TECH-003` acceptance scenarios fully implemented.
4. **Schema is a test bootstrap, not a reviewed Production migration.** Immutable audit under privileged DDL operators, full backup/recovery, migration rollback, SQL role lifecycle, load/performance and retention require separate evidence and authorization.
5. **Smoke harness is a custom executable**, not yet a complete xUnit integration/security suite with detailed machine-readable coverage. The green build runs the implemented scenarios; it is not proof of all original 20 + proposed 8 test cases as independent test assertions.
6. Dependency/package and CI workflow pinning, software supply-chain attestations and actual security penetration tests remain outside this first evidence package.
7. **No Web App code exists yet.** `TRN-EP-13` and the member Web App delivery note remain the mandatory product backlog, not a released feature.

## 5. Outcome and next gate

- **Code changes for CR-01…CR-04: implemented and verified by exact-head successful CI.**
- **Code Review: independent approval PENDING**, with residual follow-ups recorded; do not merge PR #1, #2 or #3.
- If another code slice is explicitly authorized, prioritize audited access decisions, complete deny-path/audit coverage, formal test runner and multi-tenant DB controls. Do not add real member app, clinical, financial, insurance or AI behavior under the Sprint 01 authorization.
- Stage / QA Gate / Release Approval / Production: **NOT PERFORMED**.

## 6. CR-05 — audited access decisions and audit-outage fail-closed proof (2026-10-09)

- **Severity before fix: HIGH.** The public synthetic policy evaluation previously returned an ALLOW/DENY without a durable access-decision audit. Reads of synthetic consent similarly did not establish an access-decision audit before attempting the read.
- **Code remediation:** `SyntheticAccessPolicy` made assembly-internal; `SyntheticConsentStore.EvaluateAccessAsync` is the test-only audited access boundary. It writes an allowlisted `ACCESS_POLICY_TEST` row with **decision ALLOW or DENY**, synthetic opaque actor/tenant references and a fixed synthetic correlation marker **before the policy decision is returned**. It does not persist input actions, medical data, untrusted request bodies or real member identifiers. Consent read and mutation methods call this audited access path.
- **Failure semantics:** unsuccessful access-audit write results in `AUDIT_UNAVAILABLE` (DENY), not an unlogged ALLOW. Reads of synthetic consent then return false; synthetic consent mutation is rejected. The existing mutation+domain-audit database transaction remains independently atomic, with the failure-injection test restricted to domain audit inserts.
- **Real tests:** tests assert persisted ALLOW and DENY records (including anonymous, sponsor and cross-subject denial), read rejection when audit is unavailable, denial of mutation under the same injected failure, and preservation of mutation rollback proof.
- **Reviewed code SHA:** `c50044f9bab90c27311fda5912f5b0334da82ab8`. Both exact-SHA GitHub Actions succeeded: [PR CI 37954557243](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37954557243) and [Push CI 37954549116](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37954549116).

### What remains explicitly open

- **Not independent approval:** this is the same implementation assistant's code review/fix; a separate human or otherwise independent authorized reviewer has not approved the PR.
- Access-policy audit is **not** an exhaustive event-by-event legal audit of every rejected business input, failed database read, or consent-purpose decision; there is no production identity verification and no real patient record flow. The test-only static policy remains callable *internally* by the friend smoke test for raw policy unit checks; **the auditable store path is the sole operational entry in the synthetic fixture**.
- Machine-readable xUnit-style case coverage, all original contract cases, full audit read projection and scoping, real provider integrations, database migrations/recovery, secrets/supply-chain pinning, live health-data consent, Stage/QA/Release/Production remain unimplemented/unverified. No inference to full security certification is allowed.
- **Member Web App** stays mandatory in `TRN-EP-13`, with its own future authorized Sprint; React/TypeScript UI is not part of this change.
- **Verdict:** CR-05 fixed and actual tests green on reviewed code SHA, **formal independent Code Review is still pending**, PR #1/#2/#3 remain Draft/Open/unmerged.

## 7. CR-06 — named xUnit contract suite and reproducible XML evidence (2026-10-09)

- **Resolved verification gap:** the original custom smoke executable lacked independent, machine-readable per-case results. Added `tests/Taranom.ContractTests` (xunit.v3 `4.0.1`) with separately discovered access and PostgreSQL consent tests, including synthetic purpose separation, tenant/subject isolation, idempotency and concurrent-version safety.
- **Reviewed code SHA:** `027b955b9ec192895e5e8721e9f9014e9b7b50f7`; [PR CI 37955941964](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37955941964) and [Push CI 37955936020](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37955936020) both `SUCCESS`.
- **Test accounting:** **24 discovered, 24 passed, 0 failures/0 errors/0 skipped** in [XML report](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37955941964/artifacts/11627991665); existing smoke tests executed in addition. Added cancellation-aware async tests and analyzer-compliant assertion without suppressing warnings.
- **Precise case/gap matrix:** [TRN-SP-01 acceptance ledger](TRN-SP-01-acceptance-coverage-and-ci-evidence.md). The counts **do not prove completion of every original 20 + 8 requirement**: `A-04, B-01, C-06, X-06` remain partial, `X-08` requires automated token/PHI leak tests, plus independent review remains pending.
- **Result:** CR-06 machine-readable test evidence implemented; **Code Review formal independent approval PENDING**, PR remains Draft/Open; no Stage/QA Gate/Production, no member web-app code.
