# Sprint 250 — Panel authorization middleware integration

## Scope

Wire the panel authorization service into ASP.NET Core's authentication and authorization middleware, and verify the boundary through a real local HTTP request.

## Delivered

- Adds a host extension that places authentication middleware before authorization middleware.
- A local Kestrel integration test authenticates only a synthetic test principal, routes through the center-revenue policy, and verifies that the disconnected evidence source returns the stable 403 response without reaching the endpoint.
- CI covers middleware registration, endpoint policy enforcement, response content type, cache control, and non-disclosure of issuer/scope details.

## Gates

The test identity and route are synthetic. No identity provider, trusted grant source, route backed by production data, or panel host is configured. The registered default evidence source continues to deny when a real provider is absent. This does not enable Stage, formal QA, release, or Production.
