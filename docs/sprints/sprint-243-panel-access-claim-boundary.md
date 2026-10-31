# Sprint 243 — Panel access claim boundary

## Goal

Add a small server-side policy contract that checks an authenticated principal for one exact panel capability at one exact scope, issued by an explicitly configured trusted issuer.

## Scope delivered

- Reuses the six capability names already recorded for owner-confirmed member, center, Taranom fund, and supported-case panel surfaces.
- Requires a non-empty trusted issuer, an authenticated identity, an exact claim issuer, an exact capability, and an exact scope.
- Uses an unambiguous length-prefixed grant value so scope identifiers may contain separators.
- Denies role-only claims, anonymous identities, malformed claims, missing issuer configuration, and undefined capabilities.

## Boundaries

This is a policy primitive only. No identity provider, issuer configuration, grant issuance, route middleware, user/role mapping, or production panel endpoint is connected. A ClaimsPrincipal is trusted only when supplied by a future server authentication handler; request-supplied claims are not authentication. The capability list does not define roles or decide which people receive grants.

No real member, health, center, insurance, or financial data is used. No authorization or production readiness is asserted. Human code review remains separate from CI.
