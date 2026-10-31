# Sprint 252 — Central panel capability policy catalog

## Scope

Make the six owner-confirmed panel capabilities a single immutable ASP.NET policy catalog and verify the complete policy-to-capability matrix.

## Delivered

- The member prescription, center roster, center appointment, center revenue, Taranom fund, and supported-case finance policies now register from one catalog.
- A matrix test proves each policy accepts only its declared capability across all 36 policy/claim combinations.
- The catalog preserves capability-based, role-neutral access; it does not define roles, role hierarchy, or grant issuers.

## Gates

Tests use synthetic identities, scope, and evidence. The disconnected default evidence source continues to deny. No real authentication provider, finance data, health data, or production route is configured.
