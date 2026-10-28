# Sprint 247 — Panel grant freshness boundary

## Goal

Prevent panel access from relying on a validly shaped but stale, revoked, or unauditable grant context.

## Scope delivered

The policy now requires server-provided evidence that all of the following are current/available:

- authenticated session;
- subject-to-member/operator mapping;
- permission version;
- grant not revoked;
- audit path ready.

Missing or rejected evidence denies access with a specific internal reason. Synthetic tests cover missing evidence and both missing/rejected states for each check.

## Boundaries

No source for these facts is configured in this sprint. The freshness snapshot must be populated by trusted server-side authentication, current permission/grant, and audit authorities; request values cannot establish it. No IdP, grant store, resolver, role assignment, route, or production endpoint is added.

No real identity, health, financial, or center data is used. Passing CI is not human review, formal QA, or production authorization.
