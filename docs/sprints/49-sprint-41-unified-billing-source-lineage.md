# TRN-SP-41 — Post-service unified billing source lineage (Draft)

**2026-10-11 · Sprint 41 · stacked on Sprint 40 PR #47 · TRN-TECH-050**

## Traceability and selection
- **Business:** TRN-BIZ-048/049/050/051/052, 008/009/010/011, 029/035. Decisions provide member-visible *separation* of insurer, provider, wallet and member shares; they do not authorize official values or transfers.
- **Technical:** [TRN-TECH-050](../technical/50-unified-billing-source-lineage.md) defines strict synthetic source provenance, explicit correction lineage and denied financial side effects.
- **Backlog:** TRN-EP-07, TRN-EP-09, TRN-EP-13. Do not convert externally gated insurer/billing into fake Production.
- **Code:** add immutable read assembler to existing MemberGovernance, corresponding adversarial tests, enrich existing /member/billing RTL page and four built Chromium tests. No new generic journal, endpoint or deployment adapter.
- **Code Review:** preliminary source/privacy/financial safety COMMENT after exact-head CI. Independent legal/finance/clinical/security approval remains open.

## Acceptance and test gates
1. Unverified numbers can be represented only with synthetic source and explicit currency; unknown never becomes zero, and member's remainder is never *calculated*.
2. Each of five lanes has distinct purported lawful issuer; all historical replacements have exact per-lane parent, revision and source evidence. Dispute/withdrawal hides the number even if a former claim existed; never applies a wallet correction.
3. Source mutation, scope swap, duplicate evidence UUID, revision gap, forged real reference, mixed currency and illegal evidence state fail closed or stay visibly incompatible.
4. Built member billing page shows five unknown document-source categories, no financial/insurer action, no private request or browser storage; preview remains structurally distinct from estimates.
5. Preserve all twelve inherited CI job families, actual PostgreSQL, center/member Chromium, national blockers and external-AI policy.

## Evidence pending
Exact final SHA, workflow run IDs, job outcomes, xUnit and Chromium counts and preliminary COMMENT will be recorded only after they can be verified. Draft/Open/Unmerged throughout; no Stage or Production.
