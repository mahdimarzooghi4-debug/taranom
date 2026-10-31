# TRN-TECH-243 — Panel access claim boundary

## Contract

`PanelAccessPolicy.Evaluate` evaluates a server-provided `ClaimsPrincipal`, an explicitly configured trusted issuer, a requested scope reference, and one `PanelCapability`. It allows only when an authenticated identity contains the exact grant claim type, exact issuer, exact scope, and exact capability.

Grant values use `v1|<scope-length>|<scope>|<capability-name>`. Scope length is measured in .NET UTF-16 characters; parsing is bounded and requires the separator immediately after the declared scope. This avoids delimiter ambiguity while keeping the encoded value canonical.

## Deny behavior

Missing principal, blank issuer configuration, invalid scope, undefined capability, unauthenticated identity, role-only claims, mismatched issuer/scope/capability, and malformed grant values deny. Comparisons use ordinal, case-sensitive matching.

## Authority boundary

This code does not authenticate a principal or issue grants. Only a future server authentication handler can establish that the supplied principal and claim issuer are trustworthy. Sprint 243 adds no handler, issuer configuration, middleware, routes, role hierarchy, access assignments, or production connection. The six capability names reflect the recorded panel scope; they do not determine who receives a capability.

Tests use only synthetic claims. Passing these tests is not formal QA, human review, legal authority, or production authorization.
