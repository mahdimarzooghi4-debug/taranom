# Sprint 254 — Deny unbound panel endpoints by default

## Scope

Ensure panel hosts fail closed when an endpoint omits a named capability policy or uses generic authorization without selecting a panel capability.

## Delivered

- The panel authorization registration now applies an unbound-endpoint requirement as both ASP.NET's default and fallback policy.
- Registered named capability policies continue to authorize through the existing evidence handler.
- Synthetic Kestrel tests verify that an unbound endpoint returns 403 for an authenticated principal and 401 for an anonymous principal, and never reaches the endpoint.
- The existing exact-capability HTTP matrix remains in place.

## Gates

This is a deny-only default for hosts that opt into Taranom panel authorization. Tests use synthetic identities, evidence, and endpoints. No production panel host, authentication provider, role hierarchy, or real evidence source is configured. The default evidence source remains disconnected.
