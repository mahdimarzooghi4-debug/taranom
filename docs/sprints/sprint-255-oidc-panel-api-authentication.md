# Sprint 255 — OIDC panel API authentication foundation

## Scope

Add a reusable, configuration-driven OIDC JWT validation boundary for future .NET panel API hosts without enabling panel permissions or inventing data endpoints.

## Delivered

- Require an HTTPS discovery authority, exact trusted issuer, and bounded API audience at service registration.
- Validate JWT issuer, audience, signature, lifetime, and expiration; disable claim remapping, token saving, and detailed authentication errors.
- Register the current capability catalog with default/fallback denial for unbound endpoints.
- Keep the disconnected evidence provider as the default, so authenticated requests still cannot reach protected panel actions without a server-resolved grant, resource scope, revocation state, and audit evidence.
- Add configuration and authorization-registration tests.

## Not activated

No API host, BFF, UI connection, member/center read model, endpoint, role hierarchy, issuer deployment, service secret, or production access is created. Frontend previews remain disconnected until reviewed API contracts and domain-owned data sources are implemented.

## Verification and gates

Local .NET SDK is unavailable in the workspace. Require exact-head full CI success and technical Code Review. This work does not authorize Stage, formal QA, release, Production, or Merge.
