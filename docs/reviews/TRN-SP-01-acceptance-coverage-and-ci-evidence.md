# TRN-SP-01 — Acceptance coverage ledger and exact-head CI evidence

- **Date:** 2026-10-09
- **Status:** CI-GREEN TEST EVIDENCE; **NOT full 20+8 contract acceptance**, **NOT independent Code Review approval**.
- **Code SHA under test:** `027b955b9ec192895e5e8721e9f9014e9b7b50f7`; [PR #3](https://github.com/mahdimarzooghi4-debug/taranom/pull/3) remains Draft/Open stacked on PR #2; PR #1/#2 unmerged.
- **CI evidence:** [PR workflow 37955941964](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37955941964) and [Push workflow 37955936020](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37955936020), both `SUCCESS` on the exact code SHA.
- **Machine-readable report:** [xUnit artifact 11627991665](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37955941964/artifacts/11627991665) (`sprint01-contract-tests.xml`): **24 independently discovered test cases, 24 passed, 0 failed, 0 errors, 0 skipped**. Also retains the preexisting synthetic PostgreSQL smoke harness (not counted in these 24).
- Runner: pinned `xunit.v3 4.0.1` and .NET 10; CI builds both test projects, executes real PostgreSQL smoke first and xUnit next, asserts nonempty XML and publishes CI artifact. The first build failed **xUnit1051/XUnit2031 analyzer warnings-as-errors**; propagated `TestContext.Current.CancellationToken` through asynchronous calls and corrected the filtering assertion. The later SHA is green; do not erase earlier failure evidence.

## Named acceptance coverage (design test IDs are not counts of executable cases)

| Contract case(s) | CI evidence | Verdict for Sprint 01 synthetic-only scope |
|---|---|---|
| `ID-01…ID-08` | xUnit `AccessPolicyTests` (including 3 invalid-reference variants) + PostgreSQL Smoke | **Implemented for synthetic references**; not real OIDC/IdP |
| `C-01…C-05` | xUnit `ConsentContractTests`, PostgreSQL Smoke | **Implemented in synthetic consent**; not legal clinical consent |
| `C-06` | xUnit unknown purpose/cost-choice rejection + project type separation | **Partial:** ensures no cost-choice can be passed as test consent purpose; full member cost-choice workflow not in this Sprint |
| `A-01` | Smoke persist audit on mutation; audited read/deny; xUnit policy-audit checks | **Partial:** append demonstrated; authorized audit projection incomplete |
| `A-02` | Smoke direct SQL UPDATE/DELETE attempts as owner and restricted runtime | **Implemented synthetic DB guard**; DBA/DDL and backups not immutable |
| `A-03` | Smoke fault-injected audit INSERT failure with transaction rollback | **Implemented synthetic atomicity test** |
| `A-04` | Smoke access denied/allowed & audit failure injection; xUnit audit decision count | **Partial:** allowlisted test metadata and failure behavior checked; raw log/secret redaction suite still missing |
| `A-05` | xUnit exact-replay test + Smoke duplicate audit count | **Implemented synthetic replay, no duplicate domain effect** |
| `B-01` | CI static absence checks for deployed API/AI/clinical/payment projects | **Partial:** no real integration present; not a Production artifact or deploy gate |
| `X-01` | xUnit concurrent mutations + Smoke | **Implemented test on PostgreSQL row-lock/version conflict** |
| `X-02` | xUnit changed-payload replay test + Smoke typed rejection | **Implemented in test scope** |
| `X-03` | Smoke audit failure rollback | **Implemented test** |
| `X-04` | Smoke runtime PostgreSQL role cannot delete/update or drop trigger | **Implemented test; privileged DDL remains outside test** |
| `X-05` | xUnit same-member different-tenant + Smoke withdrawal state | **Implemented synthetic tenant/scope isolation** |
| `X-06` | CI requires no deployable API, tests only friend assembly; xUnit rejects untrusted claims | **Partial:** no Production deployment package proving noninclusion |
| `X-07` | xUnit same-tenant sponsor rejection + Smoke | **Implemented test** |
| `X-08` | Audit allowlist review, no raw request persistence | **OPEN:** no automated sensitive-token/PHI poisoning and log-leak tests yet |

## Residual gates

1. **Independent reviewer:** GitHub PR #3 has **no submitted independent reviews** as of this ledger; a source review by the same assistant is not independent approval.
2. **Completeness:** original `20 + 8` are design identifiers, not the number of automated tests; xUnit discovers 24 cases, and the legacy smoke harness separately covers further behaviors. Partial/Open rows above must remain visible.
3. **Security boundary:** the system is synthetic test-only and must never be represented as a lawful identity, medical-consent, insurance or member-wallet runtime.
4. **CI robustness:** tool/package/Action/container tags are not all immutable-digest pinned; dependency lock/SBOM, log-redaction, RLS/tenancy at Production, disaster recovery and test isolation require separate reviewed work.
5. **Member Web App:** mandatory `TRN-EP-13`, not implemented and **not authorized in Sprint 01**. The next member frontend Sprint requires its own Code authorization and actual read-model contracts; no fake wallet/insurer/provider data.

**Do not Merge #1/#2/#3, mark Ready for Review, enter Stage/QA/Release or deploy Production.**
