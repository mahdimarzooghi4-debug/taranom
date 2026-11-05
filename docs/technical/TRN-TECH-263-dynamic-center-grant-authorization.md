# TRN-TECH-263 — Dynamic center grant authorization

## Contract

OIDC authenticates the caller and supplies the trusted issuer and subject. The central grant registry supplies dynamic panel permissions. Each protected API request resolves an active row matching issuer, subject, exact capability, and exact resource scope, then checks the current evidence flags and resource ownership.

The API does not accept a capability or scope grant from token claims, request headers, or body fields. Route identifiers are untrusted hints; they must equal the server-resolved grant scope. Missing, stale, ambiguous, revoked, or mismatched evidence denies access.

## Grant administration effect

A center manager with `CenterAccessGrantsManage` can grant or revoke only the allowlisted center capabilities for the same center. Changes are committed to the central registry with their audit event. Subsequent authorization reads that registry, so a grant or revocation takes effect on the next API request without Keycloak claim synchronization or token refresh. The response's `authorizationRefreshRequired` is false.

## Security boundary

A valid OIDC issuer and subject do not by themselves authorize panel access. The grant registry starts empty. Initial center-manager provisioning remains an explicit bootstrap requirement. A verified staff directory, invitation and onboarding flow, browser BFF/session, live issuer checks, and operational database controls are still required before real users can use the panel. Synthetic authentication is restricted to tests. No Stage, QA sign-off, release, or Production access is implied.
