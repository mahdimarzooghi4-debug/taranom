# Sprint 251 — Panel authorization HTTP outcome matrix

## Scope

Extend the real local ASP.NET authorization pipeline test to cover the full HTTP decision path.

## Delivered

- An unauthenticated synthetic request receives 401 even when the evidence source returns a positive snapshot.
- An authenticated synthetic request with disconnected evidence receives 403 and does not reach the endpoint.
- Exact synthetic identity, issuer, capability, freshness, and scope evidence reaches the protected endpoint.
- The test host uses an ephemeral loopback Kestrel port and is disposed after each request.

## Gates

All identities, grants, scope, route, and evidence are synthetic. No identity provider, production panel host, or real data source is configured. The default evidence provider remains deny-by-default.
