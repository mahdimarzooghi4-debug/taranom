# Sprint 248 — ASP.NET panel authorization adapter

## Goal

Connect the existing exact-capability, exact-scope policy to ASP.NET Core's authorization service without selecting an identity provider or activating real panel endpoints.

## Implementation

- Adds an authorization requirement and handler for each of the six approved panel capabilities.
- The handler delegates resource and freshness lookup to a server-side evidence source.
- The default evidence source is disconnected and returns no evidence; authorization denies.
- Missing issuer configuration, unauthenticated principals, stale evidence, and provider failures deny.
- Adds tests through `IAuthorizationService`, including proof that the endpoint resource object is passed to the evidence source.

## Boundaries

No panel routes are registered in the application, and no authentication scheme, issuer, permission store, scope resolver, or audit service is connected. The handler and policy registration are reusable server infrastructure only. An implementation of the evidence source must resolve identity, grants, audit readiness, and resource ownership from trusted server-side authorities, never request hints.

Test inputs are synthetic. CI is not human review, formal QA, or production authorization.
