# Sprint 253 — Capability-bound panel endpoint conventions

## Scope

Connect endpoint declarations directly to the central panel capability catalog and exercise the ASP.NET authorization middleware over HTTP for every catalog entry.

## Delivered

- Added a typed endpoint convention that resolves a capability to its registered policy; endpoint code no longer needs to duplicate policy-name strings.
- Unregistered capability values fail during endpoint configuration.
- Added a synthetic Kestrel matrix covering all 36 authenticated capability/endpoint combinations: only the exact capability reaches its endpoint; all cross-capability requests receive 403.

## Gates

The host, identity, claims, resource, and evidence are synthetic test fixtures. No production routes, authentication provider, role hierarchy, resource resolver, grant issuer, or evidence source is configured. The production evidence provider remains disconnected and deny-by-default.
