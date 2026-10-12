# TRN-SP-02 — Member Web App Foundation / وب‌اپ عضو

- Date: 2026-10-09
- **Status: PLANNING ONLY / CODE NOT AUTHORIZED**
- Parent Epic: `TRN-EP-13` (mandatory member-facing Web App)
- Technical companion: [TRN-TECH-011](../technical/10-member-web-app-interface-contract-candidate.md).
- Draft planning PR is documentation-only. Code Review of Sprint 01 remains independently unapproved and all parent PRs remain Draft/Open/unmerged.

## Objective

Deliver the **member-facing product shell** as a future authorized slice, in Persian/RTL and responsive Web, based on typed interfaces owned by the .NET operational core. A comprehensive first official nationwide Taranom release remains gated on the **entire** product's licensed, tested and connected readiness; this is not a public soft launch.

## In scope only after separate Code Authorization

1. React/TypeScript project with reproducible dependencies and CI, routes and protected-layout shell.
2. Design tokens/components for Persian, RTL, keyboard accessibility, mobile responsiveness and well-defined safe unavailable/loading/denied/error states.
3. A client-only contract adapter that **does not invent** real server APIs or user roles; schema-validation and explicit backend owner/version/source placeholders that remain **not connected** until real contracts are approved.
4. Local **preview-only** synthetic UI fixtures if separately approved, completely excluded from any public/production build and always labeled DEMO. Never impersonate a real member session.
5. Browser-level tests: navigation, unauthorized deep-link protection, hidden sensitive info, fail-closed errors, RTL keyboard testing, source-of-truth boundaries.
6. Document a read-model-by-read-model dependency checklist for real integration: Identity/Session, Membership, Member Credit, Clinical/Consent, Cost/Billing, Provider, Support.

## Explicitly out of scope

- No real login until contracted IdP/real session security approved; Sprint 01 SyntheticPrincipal cannot be used for real membership.
- No real wallet balance, payment, insurer determination, clinical record, provider selection, diagnosis, prescriptions or AI inference.
- No new `GET /api/...` endpoints without .NET owner-approved technical contracts and Code Authorization.
- No fake licensed providers, invoices, national identity verification, data consent, personal datasets, or financial settlement.
- No Stage, QA gate admission, official national release or Production deployment.

## Prioritized candidate implementation work packages

| Workstream | Design/acceptance outcome | Gate |
|---|---|---|
| SP02-01 | Version-controlled React/TS app shell, routing, dependency locks and UI CI | Separate Code Authorization |
| SP02-02 | Persian RTL, responsive accessible navigation and safe app states | WEB-01/04/11 |
| SP02-03 | Typed owner read-model interface, all disconnected/missing-data scenarios | WEB-03/05/06/07 |
| SP02-04 | Member data and sponsor isolation tests, browser storage/log protections | WEB-02/09/10 |
| SP02-05 | Preview fixture isolation and absence from real-release artifacts | WEB-12 |
| SP02-06 | Integration-ready dependency map, validated real API contracts when available | owner contracts and external legal/provider gates |

The candidate navigation covers membership, credit, consents, care, clinical records, costs, billing and support. **Routes are browser-only sketches**. A screen cannot present live authority without its real authorized Backend read model. Missing data stays unavailable/unknown.

## Definition of Done for an eventual code slice

- Reproducible dependencies, successful Web build, tests and CI on the exact PR SHA.
- Automated security/RTL/a11y/browser tests for approved scenarios and exclusion of preview fixtures from any releasable build.
- Independent Code Review; Stage/QA only after explicit approval, never implied by green CI.
- No misleading member account, fake wallet or unlicensed medical claim.
- A clear traceability chain from `TRN-EP-13` to each implemented member view, Backend owner contract, and test evidence.

## Current blockers

1. **Sprint 02 code approval has not been issued.** This draft plan does not create permission by implication.
2. No real OIDC/member session, service and owner read APIs, production member data, clinical/finance/insurance contracts.
3. Final React/TS/Vite/Node versions, concrete UI architecture/testing harness and browser/hosting target are proposed, not verified in this repo.
4. Sprint 01 PR #3 has no submitted independent Code Review approval; no Merge or Stage.

**Next decision point: explicit authorization for SP02-01…05 only (member shell and safe interface foundation, without real integration or fake Production data), after technical review.**
