# Sprint 262 — Center-scoped staff access grants

## Goal

Implement the owner-confirmed rule that each medical center's authorized manager administers staff access only within that same center.

## Delivery

- Add the scope-bound `CenterAccessGrantsManage` capability and exact center-scoped authorization.
- Allow an authorized center manager to list current staff capability grants, grant one supported center capability, and revoke it.
- Restrict assignable capabilities to center access administration, roster management, appointment management, and center revenue read. Member, fund and other-center capabilities are rejected.
- Record grant and revocation reasons, actor, target, center scope, capability, and request ID in a separate audit table.
- Serialize matching idempotency requests, replay identical commands safely, and reject reuse of a request ID for a different command.
- Add HTTP integration coverage for grant, idempotent replay/conflict, cross-center denial, unsupported member capability rejection, audit entries, and immediate denial after revocation.

## Limits and remaining

- The central registry remains empty by default. Initial `CenterAccessGrantsManage` assignments still require an approved bootstrap process.
- OIDC authenticates identity; dynamic permissions are resolved from the central grant registry on each request. New grants and revocations take effect immediately without token refresh.
- Staff are addressed by opaque OIDC subject; a verified staff directory and invitation/onboarding UI are not included.
- Draft/Open only. No merge, Stage, formal QA, release or Production deployment.
