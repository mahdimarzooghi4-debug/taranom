# TRN-TECH-013 — Single clinical center, departments and doctor affiliations

**Status:** Internal executable technical contract, version 1, for Draft Sprint 04 only. Approved Business source is [TRN-BIZ-054 in Draft PR #10](https://github.com/mahdimarzooghi4-debug/taranom/pull/10), completing TRN-BIZ-053. No real licenses, registrations or medical authority exist in this code.

## Bounded context
- An independently owned **ClinicalCenterGovernance** .NET assembly, not part of Identity, Member wallet, Provider Match, medical records, insurer, or care AI.
- **One clinical center → many departments**. Even a chain hospital is one center with several departments in this product model. There is no `Network`, parent center, chain admin, or inter-center role or UI.
- A center representative may record an **unverified affiliated physician** and a physician-to-department association. Every change is scoped to the one center reference. No creation of a real person, clinical privilege, confirmed license, active provider registry status, payment entitlement, medical consent, wallet rule or treatment authorization.
- Clinical access and provider qualification require independently attested, legally authorized domain owners and explicit further review. A center's recording a physician must never be enough.

## Test-only commands
`DefineDepartment`, `RecordDoctorAffiliation`, `AssignDoctorToDepartment`, `EndDoctorAffiliation`. All internal, synthetic-only `test:` refs. Commands carry an opaque request UUID, expected center version, same-center scope hints, center-manager-authority and audit-readiness evidence. The latter are untrusted caller-supplied flags in this prototype and must NEVER become a production identity system.

Results: `Applied`, `Replayed`, `Invalid`, `ConflictingReplay`, `ScopeDenied`, `EvidenceDenied`, `EvidenceMissing`, `StaleRevision`, `NotApplicable`. `Applied` means only the internal synthetic aggregate changed. It is **not** a physician activation, clinical privilege or approval.

Immutable center snapshot records departments, physician status `PendingIndependentVerification` or `Ended`, current department associations and append-only accepted transition events. Ending an affiliation clears the current associations but retains historical events. Closed affiliations cannot silently reactivate.

The reducer's single expected-version guard works only for pure deterministic **in-memory** execution. It **does not** guarantee real multi-replica concurrency without transactional persistence, authenticated actor/tenant binding, audit and lineage. No browser, API, database schema, external identity/provider, real data or credentials are connected.

## Acceptance / planned tests
- Multiple departments per center; one affiliated doctor can be linked to multiple departments of the same center.
- Manager of another center cannot write; no network-account shortcut; no implicit cross-center doctor access.
- Invalid or revoked authority, absent audit, stale version, mismatched resource, malformed identifiers and unknown enum fail closed.
- Same request exact replay after later events is harmless; changed replay conflicts; no duplicate affiliation/department/assignment changes state.
- Ending affiliation clears current assignments and retains immutable history; no automatic re-enablement or qualified/licensed status.
- Old Sprint 01–03 .NET, PostgreSQL and real Chromium tests still pass exact-head CI; no Stage/QA/Release/Production from this work.

Open contracts: authoritative real center/operator identity, physician registry/licence checks, professional verification, real doctor-centre evidence, multi-department role/assignment policy, current session/tenant rights, database transactions, legal privacy, expiry/revocation, admission gates, audit sink, real service APIs and data retention.
