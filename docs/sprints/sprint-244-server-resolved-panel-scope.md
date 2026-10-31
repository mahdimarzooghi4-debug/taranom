# Sprint 244 — Server-resolved panel scope

## Goal

Close the caller-controlled scope gap in Sprint 243. A panel permission check must use the exact scope resolved by the server for the resource, verify that the resource owner matches it, and treat any request scope value only as a consistency hint.

## Scope delivered

- Replaces the raw scope parameter with a named `PanelScopeContext`.
- Requires a valid server-resolved scope and matching resource-owner scope.
- Allows a request hint to be absent; when present, it must match the server-resolved scope exactly.
- Keeps the claim issuer, capability, and scope checks from Sprint 243.
- Adds synthetic tests for missing server resolution, conflicting request hints, and owner-scope mismatch.

## Boundaries

This is still a policy contract, not a resource resolver or live authorization service. No endpoint, identity provider, issuer, data store, resource ownership source, or production gate is configured. The context must be created from authenticated server-side resource resolution in any future integration; copying request input into all three fields does not establish that provenance.

No real member, health, center, insurance, or financial data is used. Passing tests is not formal QA, human review, or production authorization.
