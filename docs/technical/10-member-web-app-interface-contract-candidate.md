# TRN-TECH-011 — Member Web App / وب‌اپ عضو — Interface and Security Contract Candidate

- Date: 2026-10-09
- **Status: TECHNICAL PLANNING CANDIDATE, NOT AUTHORIZED FOR CODE**
- Owner requirement: **the member-facing Web App is mandatory (`TRN-EP-13`)**. A backend-only or admin-only delivery would not satisfy this requirement.
- Architecture: `C#/.NET` operational governance/data owners; internal Python AI is a separate future governed capability. **Frontend candidate:** React + TypeScript, Persian/RTL, responsive accessible web application. Build/package versions must be reviewed and locked before Code.
- Parent constraints: `TRN-BIZ-001…052`, `TRN-TECH-005`, `TRN-SP-01`, `TRN-EP-13`. First public/national release is **comprehensive** only after every promised legal, clinical, financial, and operations gate has actually passed.

## Member IA and proposed browser routes (UI candidates, NOT backend APIs)

| Proposed browser route | Member task | Runtime read owner / dependency | Required non-ready state |
|---|---|---|---|
| `/member` | Overview and next actions | member session, membership owner | access denied / not connected |
| `/member/membership` | View activation/expiry and 90-day waiting-period status | Membership (.NET) | no authoritative membership record |
| `/member/credit` | Read individual noncashable medical credit wallet and permitted use | Member Credit & Finance (.NET) | **never display a fabricated balance** |
| `/member/consents` | View/review independently scoped permissions | Consent & Privacy (.NET) | no legally authoritative consent integration |
| `/member/care` | Member care coordination and safety messaging | Care Coordination / legitimate providers | service not integrated, emergency messages never blocked |
| `/member/records` | View authorized clinical record references | Clinical Records and explicit lawful member permission | protected/no records available |
| `/member/costs` | Pre-care estimate, source and nonbinding uncertainty, member awareness/choice | Billing and Insurance Coordination | estimate unavailable, **never pretend final obligation** |
| `/member/billing` | Consolidated real billing/read status with legal issuer provenance | Billing owner / legal issuer | no legally issued invoice connected |
| `/member/support` | Complaints and coverage-dispute status | Quality & Complaints / insurer partner | external dispute system not connected |

**These are navigation candidates only.** They do **not** claim the existence of any URL, HTTP route, request DTO, read-model endpoint or database migration on the Backend. Any view involving real identity, clinical, insurance or financial data is blocked until its own reviewed contract and real integration.

## Contract boundary and read models — owner-agnostic proposals, NOT invented API endpoints

- **MemberSessionView**: server-verified member subject, tenant, authorized role, current session expiration and scope; **never generated from client-provided role/subject**. The actual IdP and OIDC contract remain OPEN. No hardcoded successful login.
- **MemberOverviewView**: only linked approved per-domain read models, each with `AVAILABLE / NOT_CONNECTED / ACCESS_DENIED / ERROR` presentation state, provenance/version and a separate timestamp. Missing service does not become "zero credit" or "no diagnosis".
- **MembershipView**: membership authoritative status and relevant waiting-period evidence from .NET owner; never calculate eligibility from a UI-estimated timestamp alone.
- **MemberCreditView**: authoritative noncashable available-balance projection, allowed uses and issuer provenance **only after finance contract/owner permits it**; no balance defaults and no cashout button.
- **ConsentView**: purpose-specific independent consent (care, data-sharing, secondary research) and legally valid evidence from actual owner. Cost awareness/choice is **not** a legal medical consent.
- **Care/ClinicalView**: only minimum data explicitly authorized for the authenticated subject and clinical purpose, with access checks and audit. No physician-level authorization inferred from a member interface.
- **CostEstimateView**: validity time, inputs/provenance, estimated/range nature and nonbinding notice; insurer determination and legally issued invoice are separate source-of-truth records.
- **ComplaintView**: tenant-isolated read projection and state; any insurer decision remains insurer-owned, with no automatic wallet effect.

All read contracts require versioned schemas and typed `NOT_CONNECTED / ACCESS_DENIED / ERROR / STALE / UNKNOWN` behaviors to avoid guessing. Cache semantics for PHI and wallet must be deliberately reviewed; no unreviewed client cache or localStorage of bearer tokens, health records or wallet data.

## Security design to approve before member UI Code

1. **Authentication:** production member route access requires real server-validated OIDC identity and session policy. Prefer backend-for-frontend session/cookie pattern for Web; final cookie/CSRF/CORS/IdP/redirect details require Technical Contract. Sprint 01 test principal is **never** a real member identity; do not wire browser controls to synthetic grants.
2. **Member/tenant authorization:** backend-owner policy enforcement on every view; frontend hides disallowed actions for UX but is not the access-control authority. Sponsor never gets member PHI or individual wallet by virtue of funding.
3. **Data minimization:** no real personal/medical/financial data in test fixtures, console errors, browser storage, analytics or synthetic UI. Define secure error redaction and signed provenance where legally needed.
4. **Fail-closed:** on disconnected/errored financial, identity or care providers, UI shows unavailable status, not made-up money, medical advice, insurance authorization or eligibility.
5. **Emergency:** safety warning and parallel legitimate clinical referral do not wait for administrative choice, wallet or financial consent.
6. **AI:** no external AI API; no AI diagnosis/prescriptions; Internal Python AI runtime is not an allowed implicit frontend dependency.
7. **Preview-only fixtures:** isolated route/build profile if later approved, with explicit `DEMO / SYNTHETIC` marking, disabled in all production artifacts and inaccessible via runtime feature flags. Not a fake member session.
8. **Accessibility and localization:** Persian RTL semantics, keyboard-only flows, focus management, reduced motion, appropriate Persian typography, accessible error messages and an explicitly reviewed WCAG 2.2 AA test target. Store absolute timestamps in standard timezone-aware form; Persian display must not alter business timing rules.

## Acceptance gates / negative test candidates

| Case | Required UI outcome |
|---|---|
| WEB-01 | Anonymous deep link never reveals member-only content |
| WEB-02 | Sponsor session never exposes member clinical/wallet data |
| WEB-03 | Changed client member/tenant identifier cannot switch backend scope |
| WEB-04 | Missing legal member/IdP connection shows blocked state, not "logged in" |
| WEB-05 | Absent wallet source never shows zero or synthetic real-money figure |
| WEB-06 | Missing coverage/clinic contract never appears as insurer/provider approval |
| WEB-07 | Cost estimate distinctly labeled nonbinding; legal invoice and cost choice remain separate |
| WEB-08 | Healthcare emergency guidance not delayed by administrative/payment consent flow |
| WEB-09 | Consent purposes remain independent; secondary AI training is not opt-in by default |
| WEB-10 | Sensitive tokens/PHI never enter browser persistent storage/logs |
| WEB-11 | RTL keyboard/screen-reader and mobile viewport smoke tests pass |
| WEB-12 | Preview-only mock layer absent from production build |

Tests are **proposals only**; no React code, Web UI, backend read endpoints, OIDC, screenshots, real data or E2E CI exists in this branch.

## Dependency and governance

- **Sprint 01 remains a synthetic security foundation**, not an identity integration enabling member login. Code PR #3 is Draft/Open and awaits independent reviewer approval; no Merge, Stage/QA/Release/Production.
- The first member Web App code slice can build a **real app shell, navigation, accessibility and typed unavailable states**, but **cannot assert real membership/financial/clinical capabilities until owner contracts exist**.
- A separate **explicit Sprint 02 Code Authorization** is mandatory before generating React application code or an integration BFF, and no public launch occurs from a partial Sprint.
