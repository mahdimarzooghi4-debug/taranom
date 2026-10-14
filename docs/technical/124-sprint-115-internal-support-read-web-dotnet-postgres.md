# TRN-TECH-124 — Sprint 115 internal support read, Web ↔ .NET ↔ PostgreSQL

Status: **isolated CI/demo vertical slice only**. Intent: advance modules we own without waiting for external APIs. No new business decision or status transition.

## Existing authority and reuse
Reuse the existing TRN-TECH-075 test-only support read endpoint `GET /internal/demo/v1/support/{caseRef}` and its existing PostgreSQL event journal. Wire an RTL React viewer into the existing localhost-only Center Demo UI. The existing .NET process requires `CI_EPHEMERAL_ONLY` plus isolated `taranom_sprint64_center` loopback PostgreSQL. Do not create a parallel support ledger or invent API contracts.

## Acceptance
- Browser GET reaches actual ASP.NET Core and PostgreSQL, displays a read-only version, topic and unverified follow-up count, and clearly states no real authorization or legal effect.
- Client rejects missing/modified authority-denial flags, wrong case ref, malformed revisions and follow-up structures, never infers real identity or release admission.
- Invalid `test:` references issue no HTTP request; errors remove prior results; late responses after the reference changes cannot reintroduce stale data.
- Playwright tests exercise real local response, hostile response, input rejection and outage, plus full repository CI. Never access public network or external provider.
- No member login/BFF, provider permission, clinical/insurance decisions, official complaint submission, payments or Production. National release remains `DENIED_BY_DEFAULT`.

## Remaining blocker
A test-only surface cannot be promoted to operational use without the versioned authenticated actor/tenant/consent contract in TRN-TECH-012, human authorization and separate release gates. The future internal production workflow must not reuse anonymous demo authority as a shortcut.
