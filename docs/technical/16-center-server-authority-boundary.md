# TRN-TECH-016 — Center manager authority: fail-closed server-owned evidence seam

**Date:** 2026-10-10. **State:** internal test-only contractual code, not identity source selection, approved operational endpoint, credential or runtime authorization.
**Business:** TRN-BIZ-053 and TRN-BIZ-054 in Draft PR #10 — ONE multi-department center; a center representative may define the center's doctors and departments, not another center's; no network/chain parent portal.
**Dependencies:** PR #11 pure center domain, PR #12 disconnected RTL center UI, PR #13 synthetic-only PostgreSQL journal. All remain separate Draft PRs.

## Threat model and trust chain

Browser-provided `center`, `actor`, `tenant`, `role`, `authorized=true`, `Confirmed`, unverified session claims or organization sponsorship **are not authority**. The .NET **server** (when legitimately connected in a later reviewed Sprint) must independently resolve and validate:
- actual authenticated subject bound to a still-current issuer/session and identity mapping;
- subject-to-center representative delegation and current operation-specific grant from the authoritative center owner;
- center identity, tenant, and the requested resource center from **server** data owners; every center/tenant relationship must match;
- current nonrevoked permission version (not a stale cached/browser value), audit availability, and per-operation authorization;
- future official physician registry/professional license is separate from merely recording an unverified doctor affiliation.

An authorized read/write still requires independently signed and reviewed domain legal/privacy contracts and a full audit trail. A *provider outage* or *missing/denied evidence* must fail closed, never fallback to member, sponsor or center-admin access.

## Implemented internal code and outcome semantics

New isolated `Taranom.CenterAuthorityBoundary` (.NET), with `ICenterServerEvidenceSource` as a **missing production adapter seam**. The default `CenterAuthorityGate` has no source and returns `AuthoritativeSourceUnavailable` for syntactically valid test-only requests. It never reads patient/provider data or calls `PgClinicalCenterJournal`.

Seven operation-specific intents exist only as private candidate vocabulary within the test-only library: department/doctor/audit reads plus DefineDepartment, RecordDoctorAffiliation, AssignDoctorToDepartment, EndDoctorAffiliation. They do not create HTTP routes, DTO names, OAuth scopes, RBAC role IDs or clinic capabilities.

The supplied test source returns `CenterServerEvidence`; security tests simulate its missing, rejected and conflicting claims. **A test stub is not an attested IdP.** Even with every test flag `Confirmed` and every synthetic ref matching, the strongest possible outcome is **`PreconditionsPresentOnly`, NEVER Authorized/Grant/Permit**. The consumer must not translate this into permission to read or write.

Protective taxonomy is internal and still candidate: InvalidRequest, AuthoritativeSourceUnavailable, IdentityNotVerified, CenterScopeDenied, ManagerAuthorityDenied, OperationNotPermitted, PermissionStaleOrRevoked, AuditUnavailable, PreconditionsPresentOnly. These are not public wire/API codes or user messages.

`BrowserRoleHint` is deliberately ignored. Subject/center/tenant hints are optional cross-checks that can only deny on mismatch, never select server identity. Doctor/sponsor/employer/admin categories cannot assume center representative rights. Matching role alone never grants permission. Permission versions and exact requested operation are checked independently; no default role or permission policy is invented. The code limits refs to synthetic `test:` and has no national IDs, tokens, usernames, medical or financial fields.

## Next admission before real integration

No Production center-manager IdP/issuer, trust anchors, live tenant/center registry, legal center manager delegation, active permission source, anti-CSRF, session revocation, audit sink, physician professional qualification service, actual center read model, request/response schemas, service topology or data-protection contract has been chosen. No permission may be activated without **explicit decisions and real adapter-backed negative integration tests** for those dependencies.

The old synthetic PostgreSQL journal has direct friend-test-only entrypoints and still accepts caller-supplied checks; it **must never be exposed as a live center service** or called merely because this gate says prerequisites present. The new test-only seam is not a substitute for a real security adapter, independently authorized HTTP commands, row-level database scope or true operational audit.

## Acceptance

Independent xUnit negative matrix for no source, outage, stale grants, expired/revoked sessions, identity mismatch, sponsor/employer/physician/admin impersonation, cross-center and cross-tenant reads/writes, forged browser role/hints, purpose-specific permissions and unavailable audit. Do not turn `PreconditionsPresentOnly` into an `Authorized` result. Repeat all prior seven independent CI jobs including PostgreSQL and real Chromium before saying Code gate green.

**No Stage, formal QA Gate, independent reviewer approval, Release or Production is authorized by this contract.**
