# TRN-TECH-244 — Server-resolved panel scope

## Contract

`PanelAccessPolicy.Evaluate` now accepts a `PanelScopeContext` containing:

- `ServerResolvedScopeRef`: scope obtained by server-side resolution of the target resource;
- `ResourceOwnerScopeRef`: scope the authoritative resource record identifies as its owner;
- `RequestScopeHint`: optional, untrusted value supplied with the request.

The policy denies when server resolution is absent or invalid, the resource owner does not exactly match, or a supplied request hint differs. The grant's scope must then exactly match the server-resolved scope, along with the configured trusted issuer and capability.

## Provenance limit

The policy cannot prove how a context was constructed. A future server integration must populate it from an authenticated resource resolver and must not copy request parameters into the server-resolved or owner fields. Sprint 244 adds no resolver, endpoint, identity provider, issuer configuration, role mapping, or production wiring.

Tests use synthetic claims and references only. They establish policy behavior for supplied inputs, not that production identity or resource provenance is trustworthy.
