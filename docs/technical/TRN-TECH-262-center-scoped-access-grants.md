# TRN-TECH-262 — Center-scoped staff access grants

`CenterAccessGrantsManage` is resolved from the central grant registry against the authenticated issuer, subject, and exact `centerRef` route scope. A manager cannot use the endpoint for another center, and request bodies contain no authority or scope selector.

The manager can list active grants and create or revoke one grant for an opaque subject under that same center. Delegable capabilities are explicitly allowlisted: `CenterAccessGrantsManage`, `CenterRosterManage`, `CenterAppointmentsManage`, and `CenterRevenueRead`. Member and Taranom-wide capabilities are rejected. A manager cannot grant access to their own subject. Grant and revoke commands require a unique request ID and a bounded reason; an identical retry replays the original result, while reuse with different command data returns conflict.

Grant state changes and audit events commit in one PostgreSQL transaction. The audit row stores actor issuer/subject, target issuer/subject, center, capability, action, reason, and request ID. Application routes provide no audit update or delete operation. Revocation changes the central grant state immediately, so a previously issued token cannot pass the central authorization check once revoked.

The manager's explicit assignment confirms the target-subject-to-center mapping. Session currency is derived from the validated OIDC request on every API call rather than stored as a durable `session_current` assertion.

This API does not mint or synchronize the target's signed capability claim. The central manager capability must be bootstrapped by an approved process, and the target's OIDC provider must issue the exact scope/capability claim before the target can use protected routes. No staff profile, phone number, national identifier, or clinical data is stored here.
