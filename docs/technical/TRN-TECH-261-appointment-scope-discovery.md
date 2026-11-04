# TRN-TECH-261 — Authenticated appointment scope discovery

`GET /api/v1/me/appointment-scopes` requires an authenticated OIDC principal. It uses the validated principal's `iss` and `sub` to query the central grant registry. The request carries no member or center identifier that can select another subject's access. Before returning each row, the API also requires the token's exact signed capability/scope grant claim to match that current database grant.

The result contains only `CenterAppointmentsManage` and `MemberAppointmentsManage` rows whose grant state is active and whose subject mapping, permission version, revocation, and audit evidence flags are current, and whose signed capability claim matches. Session currency is established by successful OIDC validation on the request. Rows are ordered by capability and opaque scope reference. The response is marked `Cache-Control: no-store`; it contains no names, clinical data, or appointment records.

The route uses a dedicated authenticated-only policy because scope selection must happen before a concrete panel capability and scope are known. Every data route continues to require its exact capability and scope-specific authorization. An empty result is expected until the central grant registry and OIDC token claims agree; this endpoint never provisions access.
