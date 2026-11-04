# TRN-TECH-261 — Authenticated appointment scope discovery

`GET /api/v1/me/appointment-scopes` requires an authenticated OIDC principal. It uses the validated principal's `iss` and `sub` to query the central grant registry. The request carries no member or center identifier, role, or capability claim that can select another subject's access.

The result contains only `CenterAppointmentsManage` and `MemberAppointmentsManage` rows whose grant state is active and whose session, subject mapping, permission version, revocation, and audit evidence flags are all current. Rows are ordered by capability and opaque scope reference. The response is marked `Cache-Control: no-store`; it contains no names, clinical data, or appointment records.

The route uses a dedicated authenticated-only policy because scope selection must happen before a concrete panel capability and scope are known. Every data route continues to require its exact capability and scope-specific authorization. An empty result is expected until a central grant administration path provisions valid access; this endpoint never provisions access.
