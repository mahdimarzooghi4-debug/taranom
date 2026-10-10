# Sprint 03 — Internal self-service member-read scope, deny-by-default

**Scope:** technical-only internal .NET 10 code in existing Draft PR #9, 2026-10-10. User authorized code progression. This is NOT a new real member identity, session, provider credential, entitlement, API, or read-model approval.

## Exact contract implemented

- Internal-only `MemberSelfReadBoundary` under `Taranom.MemberGovernance`, friend-visible to xUnit tests only, returns **PreconditionsPresentOnly** at best. It cannot grant access, materialize a private payload, launch a member session or authorize a real source.
- The narrow scope is **self-service private reads by the same member**. Sponsor, employer, provider, administrator or unrecognized actor category are always denied by this self-read contract; other legitimate professional/operational accesses require separate domain-owned contracts, not a bypass of this one.
- Server-resolved member and tenant **must** exactly match the resource owner and resource tenant, case-sensitive. Browser-provided member or tenant hints can only be cross-checked, never select identity or tenant. Refs are synthetic `test:` only, rejecting arbitrary identifiers or raw national identities.
- Current session-not-revoked, identity-to-member mapping and permission-version evidence are separate required checks. The existing `ReadPrerequisiteGate` still requires authenticated session, verified subject, resource/tenant permission, operation-specific legal basis, auditable decision path and authoritative source.
- These flags are caller-supplied **for isolated contract testing**. A malicious caller can fabricate `EvidenceCheck.Confirmed`, `SelfReadActorKind.Member`, or matching synthetic refs. Therefore this code must **never** become a real permission grant or a Production authentication module. Trust must be established at a separately authorized .NET-owned adapter with verified issuer/source, member mapping, revocation and lawful data authority. The current UI remains `NOT_CONNECTED`.
- xUnit negative tests cover sponsor/employer/other actor categories, same-tenant cross-member reads, cross-tenant collisions, manipulated browser hints, invalid/real identifiers, missing consent/legal-purpose evidence, audit failure, revoked session, stale mapping/permissions, unknown enum values and positive all-prerequisites-present **without authorization**.

## Explicit gates

No real IdP/provider, PHP/medical or financial data, registration, employer private read, browser token, BFF/session, HTTP read endpoint or Production adapter introduced. Independent review Issue #6, real identity/contract Issue #8 and Draft PR review remain outstanding. Exact-head CI success is required before claiming code stage complete, and green CI cannot approve Stage/QA/Production.
