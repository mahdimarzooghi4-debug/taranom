# TRN-TECH-262 — Center-scoped staff access grants

`CenterAccessGrantsManage` is resolved from the central grant registry against the authenticated issuer, subject, and exact `centerRef` route scope. A manager cannot use the endpoint for another center, and request bodies contain no authority or scope selector.

The manager can list active grants and create or revoke one grant for an opaque subject under that same center. Delegable capabilities are explicitly allowlisted: `CenterAccessGrantsManage`, `CenterRosterManage`, `CenterAppointmentsManage`, and `CenterRevenueRead`. Member and Taranom-wide capabilities are rejected. A manager cannot grant access to their own subject. Grant and revoke commands require a unique request ID and a bounded reason; an identical retry replays the original result, while reuse with different command data returns conflict.

Grant state changes and audit events commit in one PostgreSQL transaction. The audit row stores actor issuer/subject, target issuer/subject, center, capability, action, reason, and request ID. Application routes provide no audit update or delete operation. Authorization reads the central registry on every protected request, so revocation and new grants take effect on the next request without token refresh.

OIDC authenticates the issuer and subject only. The central registry is authoritative for dynamic capabilities and scopes; no capability or scope grant claim is required in the token. Session currency comes from successful OIDC validation on each request rather than a durable `session_current` assertion.

The central manager capability must still be bootstrapped through an approved process. Staff are addressed by opaque OIDC subject; a verified staff directory, invitation/onboarding UI, and connected panel remain outstanding. No staff profile, phone number, national identifier, or clinical data is stored here.
