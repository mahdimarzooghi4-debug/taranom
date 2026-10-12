# TRN-SP-03 — Internal Membership Governance Code Slice

- Date: 2026-10-10
- Authorization: Product Owner's explicit broad Code permission in conversation on 2026-10-10; this permits work **within pre-existing business decisions and real development gates**. It is not permission to invent a connected IdP, medical/financial data, or to certify external readiness.
- Stacked implementation branch: `sprint-03-membership-domain-kernel` based on [Draft PR #7](https://github.com/mahdimarzooghi4-debug/taranom/pull/7). Keep independent [Issue #6](https://github.com/mahdimarzooghi4-debug/taranom/issues/6) and external [Issue #8](https://github.com/mahdimarzooghi4-debug/taranom/issues/8) open.

## Actual internal package

1. `Taranom.MemberGovernance` is a .NET 10, dependency-free domain contract library; no HTTP endpoint, IdP, BFF, finance, clinical, provider, insurer or AI adapter.
2. `MembershipWaitingPeriod.Observe` enforces **90 complete 24-hour periods** from **official activation time supplied by the future authoritative owner** (TRN-BIZ-015/016). Missing evidence, future activation and timestamp overflow cannot produce an elapsed result. The returned result is a **temporal observation only**: it never decides activation, entitlement, wallet amount, insurance coverage, eligibility or spending.
3. `ReadPrerequisiteGate` combines evidence for trusted server session, verified member, tenant/resource scope, operation-specific lawful basis, audit and authoritative source. It **fails closed** on missing/unknown/rejected inputs. Even all inputs present means **preconditions for a later domain-owned review only**; no private payload may be emitted and no actual identity/authorization is performed by this module.
4. Dedicated xUnit v3 test project with per-field missing/rejected probes, denied-audit and legal-purpose safeguards, 90-day exact-boundary tests, timezone/DST, absent/future timestamps and overflow checks.
5. Separately pinned `sprint-03-member-governance.yml` CI builds/tests this exact branch. Its result must be inspected for exact new SHA; documentation is not itself a test pass.

## Deliberately not implemented

- No provider identity verification, no signed authentication assertion, no live member-to-tenant mapping, no authenticated browser session, no legal activation command, no read API/DTO, and **no financial/medical/protected data**. No successful login. `PreconditionsPresent` is **not** an access grant.
- No bypass of independent PR #3/#5 Code Review, no Stage admission, no QA completion, no national Production release. Actual ID proofing/IdP and bounded-context read source remain evidence-gated as in Issue #8.

## Evidence ledger

CI/test counts and commit SHA are recorded only after verified successful exact-head workflows. This is an internal, non-connected Code slice and cannot satisfy `INT-01…INT-12` real integration tests or `WEB-02/03`.
