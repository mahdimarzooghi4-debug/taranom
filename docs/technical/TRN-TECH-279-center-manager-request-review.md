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

## Connected UI (unverified, local test boundary)

The center React route /center-portal/manager-request is reachable without an
existing center grant but requires the host's authenticated OIDC session
token. It submits only the server-owned center reference and unverified
reason. After uncertain transport it retrieves the exact request receipt by
the original UUID; it does not resubmit the POST.

The Admin panel /admin/center-manager-requests now lists claims through the
AdminCenterRegistryManage protected API, records a documented human review,
and always displays manager access as unassigned. Unknown review results
never trigger automatic retry. Browser Chromium regressions cover these
flows, unauthenticated users, denial and invalid responses. The host
OIDC browser session itself is still not provisioned; CI tokens are test-only.

## Center-manager privilege escalation and patient publication hardening

The center-managed grant delegation allowlist now excludes
`CenterAccessGrantsManage` for BOTH grant and revoke; managers may not
create or dismiss other center managers by using the staff-grant API.
Existing manager grants remain visible to a legitimately scoped manager
for read-only governance inspection; initial appointment/succession is
still blocked pending an approved Admin-only authority contract.

Member-facing direct slot browsing and POST booking are fail-closed with
`403 CENTER_PUBLICATION_NOT_AUTHORIZED` for existing unverified centers,
even if callers know exact center/slot references and hold member scopes.
This prevents bypass of the empty public discovery catalog. Historic
member appointment reads and cancellation remain available under exact
member identity and schedule ownership checks, tested against
TEST-ONLY historical records seeded by the internal store. Future
publication requires independent qualified provider evidence and a
versioned approval contract, not a UI toggle or Admin center creation.
