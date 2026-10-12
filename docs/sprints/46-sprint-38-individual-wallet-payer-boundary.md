# TRN-SP-38 — Individual wallet ownership and contract-dependent subscription payer

**Date:** 2026-10-11. **Stacked base:** Sprint37 branch `sprint-37-disconnected-web-bundle-integrity`, Draft PR #44.

Business TRN-BIZ-024/025/026/028/029/035/036 → TRN-TECH-047 → EP-01 Identity, EP-03 Membership, EP-08 Credit & Finance and EP-13 Member Web → Sprint38 → .NET contract and regression tests → actual built Persian RTL Member UX and Chromium → exact-head CI → preliminary Code Review.

- **BL-380**: Implement real compile-checked *synthetic-only internal* `SyntheticSubscriptionPayerBoundary`, not a duplicate event journal. Typed three markets, three payer kinds, distinct individual wallet owner and sponsor payer claims, exact beneficiary/contract and tenant link; no arbitrary cross-tenant inference, financial formula or made-up contract.
- **BL-381**: A payment sponsor has **zero** implied right to view PHR, control medical credit, alter clinical consent, read private wallet or withdraw cash. A member also cannot use payer claims to bypass genuine membership activation or record/credit authorization. Four independent untrusted payment/contract evidence markers yield at most `FundingEvidencePresentOnly`. Missing/negative and mixed or malformed refs fail closed.
- **BL-382**: Populate previously generic disconnected Member `/member/membership` with three market-responsibility cards and an explicit beneficiary/owner vs payer distinction; statuses unknown, subscription action disabled. Add individual ownership and noncashable credit statement to `/member/credit`, still no balance or transaction.
- **BL-383**: Adversarial xUnit and 4 built Chromium UI/browser regressions for route matrix, private rights, unknowns, disabled buttons, history/focus and zero private calls or browser storage. Retain **12 CI jobs** and all real Postgres, internal-only AI, release DENIED, CSP/iframe, SHA-256 built output and protected clinical data DENY-ONLY tests.
- **BL-384**: exact-SHA Push+PR CI, source-level Code Review COMMENT and evidence, no independent specialist approval.

All real payment/invoice/contract, partner identity, verified membership status, approved financial backing, clinical/insurer integrations, actual covered members, AI, Stage and Production are **unavailable** and MUST NOT be simulated. PR remains Draft/Open/Unmerged and unpromoted.
