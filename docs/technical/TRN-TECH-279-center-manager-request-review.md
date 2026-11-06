# TRN-TECH-279 — Authenticated manager request and governed Admin review

## Contract

- POST /api/v1/me/center-manager-requests: identity-only OIDC; server takes actor issuer+subject from authentication, not the JSON payload; client supplies UUID, existing canonical centerRef and unverified explanatory reason.
- GET /api/v1/me/center-manager-requests: self-only, keyset paginated and no-store.
- GET /api/v1/operations/center-manager-requests: AdminCenterRegistryManage on fixed `taranom-center-registry` scope; 25 at a time, private no-store.
- POST /api/v1/operations/center-manager-requests/{requestId}/review: Admin-only; independent reviewer; one recorded immutable review, with explicit reason and unique review command ID. Decisions: REVIEWED_PENDING_VERIFICATION or DECLINED.
- No API in this slice issues CenterAccessGrantsManage or any other panel grant from request status. Review is an audit of human consideration only, *not a verified identity or manager-appointment decision*.

## Integrity

A canonical center ID is mandatory and independently checked inside the write transaction. PostgreSQL serializes submission for actor+center and uses exact request UUID/payload for replay; a different request for the same center and applicant conflicts rather than becoming a second claim. Review holds current Admin authority FOR SHARE and uses ordered transaction advisory locks for command and request. Same action/UUID replays; divergent/competing reviews conflict. Request and review history are PostgreSQL append-only (UPDATE/DELETE/TRUNCATE rejected). A claimant cannot review their own application even if granted AdminCenterRegistryManage elsewhere.

## Gates

The real OIDC identity/subject directory and authoritative proof of center representation do not exist in the deployment. User-provided reason text is UNVERIFIED; no inference about identity/legality/clinical authority is made. A future independently approved Admin bootstrap and manager provisioning contract is mandatory before any manager access is granted. Browser OIDC/BFF is still not connected. CI tests use synthetic authenticated subjects only.

No automatic grant, public center publication, certified provider discovery, actual legal authorization, production onboarding, merge, Stage, release or production behavior.
