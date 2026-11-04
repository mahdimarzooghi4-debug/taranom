# Sprint 261 — Authenticated appointment scope discovery

## Goal

Let an authenticated panel discover its own currently authorized appointment scopes from signed identity and the central grant registry, rather than asking the browser to guess or supply a member/center reference.

## Delivery

- Add `GET /api/v1/me/appointment-scopes`, protected by an authenticated-only policy.
- Resolve issuer and subject from the authenticated principal and return only that subject's active appointment grants with all five freshness/evidence flags confirmed.
- Return opaque `capability` and `scopeRef` values; return no profile or clinical information and mark the response `Cache-Control: no-store`.
- Add HTTP regression coverage for anonymous denial, current-scope discovery, and exclusion of stale, revoked, and another subject's grants.

## Limits and remaining

- The endpoint does not create or change grants. The registry remains empty unless an approved central access administration path provisions evidence.
- The endpoint discovers appointment scopes only; it does not provide human-readable member or center profiles, city/specialty directory data, or an OIDC browser session/BFF.
- Draft/Open only. No merge, Stage, formal QA, release or Production deployment.
