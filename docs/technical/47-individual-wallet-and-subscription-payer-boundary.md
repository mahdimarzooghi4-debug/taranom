# TRN-TECH-047 — Subscription payer responsibility is NOT individual membership, wallet or clinical authority

**Date:** 2026-10-11. **Parent:** Sprint37 Draft PR #44. **Business authority:** TRN-BIZ-028 (all three market paths in business strategy, not guaranteed launch), TRN-BIZ-029 (person, employer or support institution may fund a member's subscription *according to a real contract*, but membership and personal noncashable medical wallet remain with the individual), TRN-BIZ-024/025 (membership fee belongs to sixth revenue stream, not automatically direct wallet grant), TRN-BIZ-026 (equal allocation among independently eligible members, with amount/eligibility undecided), TRN-BIZ-035/036 (financial sponsor does not inherit private health data or research consent).

## Why a bounded PRODUCT contract now

The real contract/payment/identity systems are not connected. Before this Sprint, the disconnected Member Web showed only a generic no-data message for `/member/membership`, and the existing `MemberSelfReadBoundary` correctly denied sponsor reading a private member projection. However, there was no typed domain-specific statement that **paying a subscription** does not amount to **ownership of medical credit, activation of membership, access to health records or clinical consent**. This Sprint adds a pure read-only structural contract (not a new journal), and an explicit Persian RTL representation across the existing membership and credit pages.

## Domain implementation

`SyntheticSubscriptionPayerBoundary`, internal in `Taranom.MemberGovernance`, accepts only bounded `test:` identifiers. A synthetic funding claim includes:

- exactly one market: `DirectPublic`, `SupportInstitution` or `CorporateEmployer`;
- a payer kind: Member, Support Institution or Employer; the direct public route only accepts Member, while each supported institution/corporate route accepts Member or its corresponding sponsor kind;
- individually pinned `MemberRef`, `MemberTenantRef` and `IndividualWalletOwnerRef` (wallet owner MUST remain member), and exact claimed beneficiary and tenant on the hypothetical contract;
- a claimed payer actor, their tenant and four **independent `EvidenceCheck` markers** for exact active contract, contractual permitted payer for fee, subscription-fee evidence and independently validated payment source;
- a named capability being requested. There is no amount, transaction ID, expiry, subscription activation event, member credit allocation, medical record, payment adapter or public API.

A sponsor may be in the same or different tenant as the member: **no unapproved organizational tenancy architecture is assumed**. The payer actor must remain distinct from the individual member actor. Wrong market, payer kind, owner, contract member/tenant, claimant or non-synthetic source yields `InvalidInput`/`ContractScopeDenied`.

A sponsor requesting `ReadMemberHealthRecord`, `ControlMemberTreatmentCredit`, `ChangeMemberClinicalConsent`, `ReadMemberPrivateWallet` or `WithdrawTreatmentCreditAsCash` receives `PayerPrivilegeDenied`, regardless of claimed payment/contract evidence. Cash withdrawal is denied even to an individual member in this non-cashable treatment-credit contract. Real membership activation and a member's access to their own private records/credit require **separate, real domain authorization**. Only `InspectSubscriptionFundingEvidence` can return `FundingEvidencePresentOnly` if ALL four untrusted claimed checks are `Confirmed`, never actual payment, membership activation, receipt, legal authority, wallet ownership transfer or treatment-credit grant. Missing/unknown markers fail closed; explicit rejection dominates missing evidence. No guessing subscription amount/rate, partner, discount, eligibility, waiting window, financial allocation, receipt or billing policy.

## Member-facing UX without invented product data

The existing disconnected `/member/membership` route now shows the three approved funding routes as **responsibility descriptions**: public/direct member, support contract member or support institution, and corporate contract member or employer. Each says **`اطلاعات موجود نیست`**; no real payer, contract, payment or active membership exists. A prominent note states that ownership of membership and treatment wallet stays with the person and an employer/support sponsor does NOT acquire treatment rights, medical files or member's consent. The only subscription CTA is disabled until verified identity, fee contract/payment and governance sources exist.

The existing `/member/credit` route clarifies individual wallet ownership, noncashability and UNKNOWN amount; disabled transaction-history button, no fabricated balance, payment or credit.

Preserve old `connect-src 'none'`, built HTTP CSP including `frame-ancestors 'none'`, no local/sessionStorage, Sprint37 deterministic bundle SHA-256, national launch DENY, clinical-data DENY-ONLY, independent research consent gate and internal-only AI restrictions.

## Tests and deployment boundary

xUnit positive/negative matrix: all permitted and forbidden market/payer pairings, all sponsor health/consent/wallet privileges denied, cash withdrawal denied, direct member access requires its own authority, activation remains separate, complete claimed evidence NEVER grants real payment/activation, each missing/rejected check, cross-subject owner/contract/tenant scope, same-tenant sponsor permitted without health rights, malformed/future enums and real IDs fail closed. 4 new Chromium cases verify all routes unknown, privacy, disabled subscription and wallet action, and no private network/browser storage even after navigation. Full inherited **12-job exact-SHA CI** (including real isolated Postgres, protected-data DENY-ONLY, external-AI/Release-denied policy gates and both built browser bundle controls); source Code Review COMMENT only.

**Not implemented or authorized:** real contracts, verified sponsor identity, employer enrollment, banking/PSP, membership activation, timing, settlement, financial allocation formula, actual wallet spend, treatment, insurer, production host, genuine PHR or legally reviewed data grant. No independent financial/legal/medical/privacy/security sign-off. PR Draft/Open/Unmerged, no Ready/Merge/Stage/QA/Release/Production.
