# Sprint 263 — Dynamic center grant authorization

## Goal

Make the central grant registry authoritative for current panel permissions, while OIDC remains the authenticated identity source.

## Implemented

- Removed dependency on capability and scope claims in long-lived OIDC tokens.
- Kept exact issuer and subject authentication, current central grant lookup, scope/resource binding, evidence checks, and deny-by-default behavior.
- Updated appointment scope discovery to return only current central grant rows.
- Made center staff grants and revocations take effect on the next request without token refresh.
- Updated HTTP and policy tests to use identity-only synthetic authentication and server-side grants.
- Updated API contract documentation.

## Verification boundary

CI must compile and execute the full regression suite on the exact PR head. Live OIDC issuer validation, manager bootstrap, verified staff onboarding, browser BFF, Stage, formal QA, release, and Production remain outside this sprint.
