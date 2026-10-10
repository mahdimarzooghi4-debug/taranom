# TRN-TECH-017 — Closed protected-data interlock for a center's private data

**Status:** Test-only internal code, not a real center-data permission or transport. Date: 2026-10-10.

Business basis: TRN-BIZ-053/054 (Draft PR #10), ONE center with multiple departments and its own unverified doctor affiliations, not a parent network. Built foundations: PR #11 domain, #12 RTL disconnected shell, #13 PostgreSQL isolated synthetic journal, #14 server-authority **precondition** gate.

## Independent trust and consent boundary

`Taranom.CenterAuthorityBoundary.CenterAuthorityGate` can only report `PreconditionsPresentOnly` even when a test source asserts every issuer/session/delegation/center/tenant/permission/audit flag is Confirmed. Its caller-provided flags are **not authoritative**. No approved real issuer, service/organization registry, center operator, tenant binding, legal role grant or consent exists. A matching browser role, synthetic ID, successful test comparison or center affiliation **must never activate private reads or writes**.

## New technical safeguard

`Taranom.CenterDataAccessInterlock` is a separate, internal, non-packable .NET assembly whose `CenterDataAdmissionInterlock` consumes the precondition gate but **always returns a rejection**. It has an injected private-data port solely to prove in tests that no read/write is invoked; there is **no production implementation** of that port and no privileged HTTP endpoint, JWT/session, direct Npgsql dependency, provider or actual patient/doctor payload in the assembly.

Results are strictly `InvalidRequest`, `NotConnected` or `Denied`. These are internal test semantic values, **not public wire status/HTTP or legal policy taxonomy**. No `Authorized`, `Success`, data DTO or mutation result exists. With fully Confirmed test evidence, result remains `NotConnected`; actual portfolio/domain data exposure and approval are impossible through this path.

The independent center journal from Sprint06 **can still be accessed by its own friend-test code** and contains untrusted caller-provided Confirmed flags. This code does NOT prove the repository is safe for Production. The test-only interlock is a defense-in-depth contract for a future authenticated application boundary; it cannot turn synthetic tests into real identity/security evidence.

## Verification

- New pure xUnit suite exercises every one of seven read/write operation intents against absent source, fake positive source, forged browser role, cross-center request, revoked grant and unavailable audit, and proves a throwing private-data test port is NEVER invoked.
- Independent real PostgreSQL 18 integration suite seeds two accepted operations (one department, one unverified physician) for unique synthetic center references. For all seven intent types, complete fake evidence, missing issuer or other-center access causes **zero private port invocations** and **no mutation** to persisted state. Reads via the isolated test journal independently verify existing stored version, department and doctor remain unchanged.
- Full prior eight jobs must still pass exact final SHA: member/center domain, authority xUnit, Sprint01/03/06 PostgreSQL and member/center Chromium, plus two new jobs = ten.

## External admission gates

Before implementing any real .NET or UI read/write, independently select and verify legitimate provider(s) for authenticated user, center operator delegation, current permission/revocation, center/tenant owner, license/qualification, privacy/consent, audit provenance, anti-CSRF, database RLS/ownership and contractual lawful use. Review an explicit service schema and migration only after these inputs are real. No timeframe, vendor, URL, token/credential, clinician, financial entitlement or business threshold may be fabricated.

**No Merge / Stage / formal QA / Release / Production.** Keep stacked PR Draft/Open pending independent code and security review.
