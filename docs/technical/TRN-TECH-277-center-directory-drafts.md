# TRN-TECH-277 — Center and doctor *unverified* directory draft registry

Figma frames: center profile 160:2; center doctors and roster 135:46 and 136:2.
Decisions: TRN-BIZ-043/044, TRN-BIZ-053/054/055.

## Scope
This increment introduces *center-controlled private draft records*, not a
medical provider directory, clinician identity proof or qualified search.

The canonical clinical-center record is Admin-created under TRN-BIZ-055.
These draft endpoints do not create a center, establish canonical identity,
provision a center-manager account/grant, or mutate the canonical center
record. Every draft is scoped to a pre-existing center reference and a current
server-side grant.
- Current OIDC issuer+subject and centrally versioned scopes stay mandatory.
- CenterAccessGrantsManage can GET/PUT its own center profile draft.
- CenterRosterManage can GET and PUT doctor claims in its own center.
  This existing capability is now mapped to the same server-side exact-scope
  enforcement as other center endpoints.
- GET /api/v1/me/center-directory-draft-scopes returns current grant scopes
  only. It confers no capability on its own.
- Center profile fields and doctor names/specialty are **center-claimed draft
  data**, not canonical center identity or official verified credentials. The
  marker UNVERIFIED_DRAFT is fixed by the backend.
- A center profile draft is only a submission by the center. It cannot change
  the Admin-defined canonical center record, create a center, or appear in the
  member-facing directory. Admin review/acceptance of profile changes is not
  implemented or specified by this contract.
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
- Admin-owned canonical center registry: center creation, canonical fields,
  stable reference issuance, Admin authorization/audit, and center-manager
  association/grant provisioning.
- Explicit Admin review/acceptance contract for center-submitted profile
  corrections; until then they remain private unverified drafts.
- Authoritative legal center/provider identity and licence evidence,
  contractual binding, and responsible human qualification workflow.
- Explicit approved publication gate for the national patient search directory,
  then city/specialty matching against approved evidence-backed records.
  Admin creation alone is not publication or service qualification.
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

## Canonical Admin registry integration (TRN-TECH-278)

TRN-BIZ-055 establishes a distinct canonical center identity, separate from
this draft store. The Admin path `/admin/centers` calls
`GET/POST /api/v1/operations/centers`; the backend independently checks
the current, exact server-held `AdminCenterRegistryManage` capability and
the fixed `taranom-center-registry` scope. It never accepts a caller-selected
centerRef or a center-created registry record.

The Admin enters only name and city, already existing in the Figma directory
design, with no legal ID/qualification claim. The server issues an opaque
centerRef and writes its creation receipt atomically. A receipt lookup by
original actor+UUID can resolve an uncertain network ACK without a second
POST. Creation explicitly returns `ADMIN_DEFINED_UNVERIFIED` and all
license, publication and manager-assignment flags false. There is no automatic
Admin grant bootstrap or public directory feed. External authoritative
manager-subject onboarding is not part of this increment.

## Admin-center resource integrity / publication gate

Center operational grants are rejected by the centralized authorization
evidence source if `scope_ref` has no matching canonical Admin-defined
center. The grant-mutation service rechecks that center in the same
transaction with a row lock; private center drafts also recheck during
mutation, and scoped center lists exclude noncanonical references.
Member-specific appointment routes require canonical existence, while a
member's own authorization remains independently required.

**No public center publication workflow is authorized yet.** The former
capacity-only member discovery endpoint is therefore fail-closed: a
currently entitled member receives an empty `centers` collection with
`publicationGateRequired=true`, even if an Admin-defined unverified center
has private open slots. It must not automatically publish the center based
on capacity, Admin creation or physician draft fields. The separate
publication decision/evidence contract is still absent.

## Immutable canonical definition and revocation boundary

The Admin-defined canonical center identity is immutable in this technical
slice. PostgreSQL rejects UPDATE, DELETE and TRUNCATE on canonical rows, not
only on creation receipts; future approved correction/closure must introduce
a separately reviewed versioned lifecycle. The center grant-mutation service
checks and holds both the canonical center row and the actor's live
`CenterAccessGrantsManage` grant with transaction-level `FOR SHARE`,
avoiding grant writes that overtake revocation after middleware evaluation.
No Admin-to-manager grant is automatically generated.

## Correct patient-facing publication status

The member UI requires an explicit `publicationGateRequired=true`
and empty `centers` payload. It shows a publication hold rather than an
incorrect "no free capacity" statement. A forged/malformed response
containing centers without an approved publication contract fails closed;
the browser tests cover authorization, home entry, mobile and malformed data.
