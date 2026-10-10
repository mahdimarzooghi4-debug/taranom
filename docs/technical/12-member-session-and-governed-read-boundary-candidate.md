# TRN-TECH-012 — Member Session and Governed Read Boundary (candidate)

- **Recorded:** 2026-10-10
- **Status:** TECHNICAL CONTRACT CANDIDATE / REVIEW REQUIRED / **NOT CODE-AUTHORIZED**.
- **Context:** `TRN-EP-13` (mandatory Member Web App), `TRN-TECH-005` (.NET operational owner, Python internal AI computation only), `TRN-TECH-011` (member interface candidate), [Sprint 02 shell PR #5](https://github.com/mahdimarzooghi4-debug/taranom/pull/5).
- **Hard fact:** Sprint 01 implements **synthetic-only** .NET identity/consent/audit; Sprint 02 implements a **disconnected** React/TS member shell. Neither creates a real member identity, IdP connection, member-facing BFF, authenticated HTTP session, domain read endpoint, verified health data or financial source.
- This document expresses security invariants and prerequisites, **not an approval of a vendor, URL, DTO, HTTP route, cookie scheme, client grant, token claim, timeout or data policy**.

## 1. Explicit trust boundary and owner responsibilities

| Layer | Permitted responsibility | Prohibited substitution |
|---|---|---|
| React/TypeScript Member Web | Accessible UI, navigation, display of verified *authorized* read projections only after contracts are implemented | Browser query/local state must never identify or impersonate member, choose tenant or authorize a read. No fake member/wallet/coverage. |
| Server-side member session boundary (**BFF candidate**; topology OPEN) | Obtain/validate real identity through approved IdP, enforce authenticated session and anti-CSRF, bind server-side subject/tenant/scope, deliver reviewed read projections | No pass-through of browser-provided `actor/role/member/tenant` as authority. No browser-held access/refresh tokens or general-purpose unrestricted proxy. |
| .NET Identity & Access / Membership | Own verified member-to-identity mapping, tenant/org context and right to act, adjudicate current member eligibility where authorized | The synthetic `SyntheticPrincipal` and Sprint 01 friend-test assembly must never become a Production identity source. Sponsor status does not imply access to member health/wallet. |
| .NET Consent & Privacy / Audit | Enforce purpose/scope/legal-evidence requirements and record authorized access/denials with minimized append-only audit | UI checkmark, membership or cost-choice must never act as lawful clinical/research consent. Audit failure must not silently permit protected operations. |
| .NET domain data owners | Return authorized, versioned, provenance-tagged, limited member read models | No client-side calculation of credit/eligibility, insurer authority, 90-day waiting period or diagnosis. No invented projection if source unavailable. |
| Internal Python AI | Only governed internal computational work when separately approved | No direct member identity, health/finance writes, external AI API or autonomous medical decisions. Not part of this Sprint candidate. |

A BFF is a **candidate** to protect browser credentials and same-origin sessions; exact .NET hosting/process topology and transport are **OPEN**. Do not assume Next.js, a separate inference service, a remote identity vendor or any concrete `/api/...` endpoint.

## 2. Session lifecycle — contract questions and mandatory denial paths

The intended **real** session sequence, after selecting legally/technically valid identity providers and data-processing authorities, is:

1. Anonymous browser remains in the current `NOT_CONNECTED` / protected-unavailable mode. A browser UI may navigate but receives **no member private payload**.
2. A trusted server boundary initiates a reviewed authentication handshake with the selected real IdP. Exact OIDC flows, callback URLs, state/nonce, anti-replay, PKCE applicability, trust roots, token lifetimes, step-up rules and identity proofing require explicit technical sign-off.
3. The server **validates** identity evidence, then independently resolves its *authoritative* Taranom member subject and allowed tenant/org association from the .NET owner. **No browser-supplied claims or sponsor contract alone establishes membership**.
4. Session state is protected at server boundary. Session/cookie policy, secure attributes, CSRF, expiration, refresh/revocation, parallel devices, login/logout audit and error handling must be agreed and tested before any real login Code. **A successful provider redirect alone is not an authorized member session.**
5. Every protected read is checked against current member identity + tenant + resource + purpose + consent + current authoritative source version. Missing context, expired/revoked session, out-of-tenant request, unavailable IdP, absent audit or unverifiable consent => DENY/UNAVAILABLE. No optimistic member view.
6. Logout/revocation must invalidate the server session; browser cleanup alone is insufficient. After expiry/logout, back-button/deep-link or stale cache cannot display protected data.

### Open admission inputs (do not invent)

- IdP/identity-verification operator, contract, issuer, registration, verified member identifier mapping, assurance and recovery policy.
- Tenant/member organizational model, sponsor-vs-member relationship and authoritative entitlement source; member profiles and roles require accountable .NET ownership.
- Whether BFF runs within the .NET API process or a separate trusted web server; physical host, DNS, TLS, same-origin topology and protection of session secrets.
- Human-approved authentication and authorization failure taxonomy, audit/legal basis, cookie/CSRF/CORS/CSP/cache rules, session lifetimes and retention requirements.
- Live service agreements for finance, clinicians, direct-contracted providers, invoices and insurance; production credentials and access rights. None exist in this plan.

**No concrete JWT claim name, endpoint path, identifier schema, cookie lifetime, OAuth scope, issuer URL or tenancy inference is approved by this proposal.**

## 3. Domain read-model admission — nine member routes

| Existing browser route | Proposed authoritative owner | Minimum acceptance evidence before changing `NOT_CONNECTED` |
|---|---|---|
| `/member` | Session owner + individual domain owners | Verified session and versioned, independently authorized per-section status; cannot collapse missing sources to empty/zero. |
| `/member/membership` | .NET Membership | Official activation, expiry and waiting-period evidence; 90 **full calendar days** rule adjudicated by authorized owner, not estimated in UI. |
| `/member/credit` | .NET Member Credit & Finance | Real account provenance, lawful individual read right, noncashable balance; insurer pays first; wallet only up to available balance and with real direct-contracted eligible providers. No cash-out. |
| `/member/consents` | .NET Consent & Privacy | Legally valid evidence for independently scoped purposes, including separate lawful permission for secondary AI usage; no preselected research consent. |
| `/member/care` | .NET Care Coordination / qualified service owner | Safety/role review, legitimate provider referral and nondiagnostic scope; emergency response not blocked by administrative/financial workflow. |
| `/member/records` | .NET Clinical Records | Source-origin, lawful individual authorization and minimum-purpose clinical read; no AI diagnosis or synthetic personal record. |
| `/member/costs` | .NET Billing + Insurance Coordination | Provenance and validity for explicitly **nonbinding estimates**; member awareness/choice separate from treatment consent or legal payment undertaking. |
| `/member/billing` | .NET Billing + lawful document issuer | Official invoice issuer/source, insurer determination separate from wallet disbursement; no locally fabricated invoice or settled state. |
| `/member/support` | .NET Quality & Complaints | Bounded member-only ticket projection; insurer adjudication and complaint decisions remain with competent authorities. |

For each route, the owning domain must sign off on: subject/tenant lookup, exact allowed fields/minimum data, provenance, schema/version, validity/staleness, partial availability, pagination, audit event, consent/retention and no-cache/security behavior. **No concrete API endpoint or DTO is approved until owner-specific Technical Contract and separate Code Authorization.**

## 4. Status and failure-state candidate — no fabricated success

Display states are conceptual **candidate semantics**, not finalized wire enum values:

- `NOT_CONNECTED`: no legally approved backend integration. Current Sprint 02 behavior stays unchanged.
- `AUTH_REQUIRED`: protected private member data requires a validated member session; no identity may be fabricated.
- `ACCESS_DENIED`: verified session exists but fails current policy/tenant/purpose gate; details of PHI must not leak through errors.
- `SOURCE_UNAVAILABLE`: an authoritative source cannot be contacted or verified; **not** equivalent to `0`, `false` or `none`.
- `UNKNOWN / STALE`: evidence absent/invalid, data might be outdated; no inference of valid coverage, membership, safe clinical status or sufficient balance.
- `AVAILABLE`: only after reviewed server-side authorization, source provenance/quality checks and all relevant domain/legal gates.

No direct client decision can move a state to `AVAILABLE`; no fallback to a mock/static array when Production data is missing.

## 5. Negative acceptance plan — cases to implement only after authorization

| ID | Controlled test evidence required |
|---|---|
| INT-01 | Unauthenticated direct URL, refresh, history and preloaded assets yield no private member payload. |
| INT-02 | User-modified browser member ID, tenant, role, route/query/headers cannot alter effective authenticated server identity or scope. |
| INT-03 | Sponsor, employer, unrelated member and cross-tenant reads fail closed; denied attempts have minimized audit evidence. |
| INT-04 | Forged/replayed/expired identity assertion and unapproved IdP cannot create/restore a real member session. |
| INT-05 | Logout, revocation, missing audit or changed permission invalidates access; historical browser cache does not re-expose private data. |
| INT-06 | Missing/withdrawn purpose-specific consent blocks only relevant protected use; care consent cannot imply research or cost commitment. |
| INT-07 | Missing finance/provider/insurer integration never displays zero credit, insurer approval, payment/settlement, or invented treatment. |
| INT-08 | Cost estimation uncertainty, insurer authority, licensed clinical authority and official invoice provenance are independently represented. |
| INT-09 | Identity/authentication errors and access logs redact secrets, PHI, national identifiers, payment details and tokens. |
| INT-10 | Same-origin/BFF cookie, CSRF, redirect, CORS/CSP and session fixation controls have real security test evidence. |
| INT-11 | Every approved route/read model has source/version, bounded pagination, subject/tenant scope and correctly handled staleness. |
| INT-12 | No test principal, preview fixture, synthetic wallet, AI inference provider or fake account appears in Production artifact. |

These are **future tests**, not current passes. Existing 16 Chromium checks prove disconnected UI only; Sprint 01's 28 synthetic xUnit cases do not prove real authorization.

## 6. Admission decision

**Current result: BLOCKED for real identity/member-data Code.** The next real integration Sprint needs all selected identity/contract inputs above, accountable .NET read-model owners, concrete reviewed transport/schema/security contract, legal/regulatory authority, explicit scope-limited Code Authorization, and exact-head tests. A technical-only preliminary slice could be proposed separately but **must not simulate a successful real login or invent Backend endpoints**.

The initial official national release remains comprehensive; progress in this slice neither constitutes Stage nor an MVP launch. PR #1–#5 stay Draft/Open/unmerged; independent Code Review is tracked in [issue #6](https://github.com/mahdimarzooghi4-debug/taranom/issues/6).
