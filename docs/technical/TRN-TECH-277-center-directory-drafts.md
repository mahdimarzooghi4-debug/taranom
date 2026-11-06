# TRN-TECH-277 — Center and doctor *unverified* directory draft registry

Figma frames: center profile 160:2; center doctors and roster 135:46 and 136:2.
Decisions: TRN-BIZ-043/044, TRN-BIZ-053/054.

## Scope
This increment introduces *center-controlled private draft records*, not a
medical provider directory, clinician identity proof or qualified search.
- Current OIDC issuer+subject and centrally versioned scopes stay mandatory.
- CenterAccessGrantsManage can GET/PUT its own center profile draft.
- CenterRosterManage can GET and PUT doctor claims in its own center.
  This existing capability is now mapped to the same server-side exact-scope
  enforcement as other center endpoints.
- GET /api/v1/me/center-directory-draft-scopes returns current grant scopes
  only. It confers no capability on its own.
- Center and doctor names/city/specialty are **center-claimed data**, not official
  verified credentials. The marker UNVERIFIED_DRAFT is fixed by the backend.
- PostgreSQL keeps center revisions and per-center doctor revisions, plus an
  append-only command audit keyed by center+actor+request ID.
- Writes recheck the current grant with a transaction-held FOR SHARE lock,
  ensuring a concurrent revocation cannot overtake the mutation commit.
  A per-center advisory transaction lock serializes competing drafts.
  Same-actor exact request ID+payload replays; changed/stale requests conflict.
- No center can mutate another center by changing a URL.
- Doctor drafts are paginated (25 items, keyset after doctorRef).
- No auto-approval, publication, clinical roles, payer/financial permission,
  public patient exposure, appointment slot mutation or medical record access.

## Frontend
/center-portal/profile and /center-portal/doctors plus related Figma doctor
routes now show scoped persisted draft forms. They do not display sample
clinician claims as saved data; revocation/error states fail closed. Missing
or uncertain writes require re-reading, never speculative resubmission.

## Verification
PostgreSQL HTTP tests: valid grant, 401/403, cross-center denial, immutable
audit and replay, stale revision, doctor roster permission distinct from center
profile, invalid input and immediate revocation. Browser tests: connected
draft forms, absence of invented verified claims, scope denial and handling
uncertain responses. These are CI tests with synthetic authenticated subjects,
not actual clinician onboarding or live identity-provider evidence.

## Next dependencies
- Authoritative licensed provider/center identity and independent evidence
  source(s), contractual binding, responsible human qualification workflow.
- Explicit approved publication gate for the national patient search directory,
  then city/specialty matching against approved evidence-backed records.
- Real OIDC browser session/BFF and trusted grant bootstrap. No deployment,
  merge, Stage, release, actual contracts, or medical permissions.

## Independently scoped session evidence
A delegated center grant begins with session_current=false; this is not a
revocation and does not establish a live-session fact for another subject.
Authenticated OIDC identity establishes request-session validity, while current
central grant records establish exact capability, resource scope, current
subject mapping, permission version, revocation and audit readiness. Draft
endpoints must not treat the grant row's initially-false session marker as
proof of missing OIDC authentication. An HTTP regression covers the actual
center-manager delegated staff path and cross-center isolation.

## Audit and browser-state hardening
- The center-directory command audit now has PostgreSQL UPDATE, DELETE and
  TRUNCATE rejection triggers; an HTTP/PostgreSQL regression verifies all
  three operations fail and the accepted row remains present.
- The browser distinguishes deterministic 4xx conflicts/denials from an
  uncertain transport/server outcome. Deterministic failures block writes
  until a fresh read; uncertain outcomes never auto-replay.
- Doctor pagination responses are bound to the currently selected center and
  request generation, preventing a delayed page from a prior center or prior
  reload from contaminating the current roster. Successful save feedback
  survives the post-write refresh.
