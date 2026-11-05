# TRN-TECH-261 — Authenticated appointment scope discovery

`GET /api/v1/me/appointment-scopes` requires an authenticated OIDC principal. It reads the validated issuer and subject, then queries the central grant registry. The request carries no member or center identifier that can select another subject's access.

The response includes only active `CenterAppointmentsManage` and `MemberAppointmentsManage` grants whose subject mapping, permission version, revocation, and audit evidence are current. OIDC validation establishes a current authenticated session for that request. Dynamic capability and scope claims are not required in the token; the current registry is the source of permission and scope.

Rows are ordered by capability and opaque scope reference. The response is marked `Cache-Control: no-store` and contains no names, clinical data, or appointment records. Data routes still check the exact current capability and scope against the registry on each request, so revocation takes effect without waiting for token expiration.

An empty result is expected until the central grant registry contains approved grants. This endpoint never provisions access.
