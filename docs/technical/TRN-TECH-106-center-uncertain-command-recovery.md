# TRN-TECH-106 — Manual exact-command recovery for uncertain center writes

**Date:** 2026-10-13 | **Sprint:** 97 | **Business:** TRN-BIZ-053/054, TRN-BIZ-028/030/040 | **Epics:** TRN-EP-04/12 | **Foundation:** TRN-TECH-073 (existing center POST idempotency), TRN-TECH-093 (manual guided center flow), TRN-TECH-104/105 (consistent source reads).

**Parent dependency:** Sprint96 Draft PR #107 HEAD `d88f8b80db3dcdc40448500e76ad93b33b4418a4`, Push #38372216944 and Mainline PR #38372222783 14/14 SUCCESS, preliminary COMMENT review only. All code remains strictly `test:`, loopback/ephemeral PostgreSQL, unverified clinical center affiliation.

## Confirmed functional defect

The center UI's `apply(kind)` generated a fresh `crypto.randomUUID()` and expected version each time. If PostgreSQL successfully committed but a browser ACK was dropped, the UI cleared its snapshot and could only issue a new command; no same-request recovery was available. Re-reading alone cannot establish that an earlier request was the one accepted if unrelated writers changed the state. A new UUID cannot prove exact server-side replay.

## Bounded command-recovery contract

- On **every** intentional new command, build exactly one immutable tuple: bounded `test:` center, initial expected version, action, department/doctor references and one original UUID. Serialize JSON **once**; use those exact bytes for initial POST and all manual retries. Do not introduce a new endpoint, journal, identity, actor, DB schema, service worker, or storage.
- Only a **recognized and structurally valid** response resolves the request: `200 Applied` at exactly original expected version + 1, or `200 Replayed` at a later source version; or explicitly rejected `409 StaleRevision/NotApplicable/ConflictingReplay` with attested center snapshot. All other HTTP codes, malformed/denied response, transport aborts and unknown outcomes leave the original command **UNCERTAIN**. The client only validates the existing DTO; it does **not** cryptographically attest a server or promise identity.
- While UNCERTAIN, clear all current/historical read-backed browser views; block any *new* center writes and guided action and lock all input identifiers. Display an accessible Persian uncertainty explanation. Manual source GET is allowed but must never conclude the original was accepted. Only an explicit human click may send the original byte-for-byte POST again. Never auto-retry.
- On a verified `Replayed` result, explain no new event. On actual `Applied`, explain one accepted event. On conclusive `409`, show source state and explain denial. On any further ambiguous response keep the *same* original receipt. A reload discards volatile receipt; warn user beforehand; no localStorage/sessionStorage/IndexedDB, URL, cookie or background persistence. This is not cross-session recovery or a real legal receipt.
- Prevent silent new commands when a stored result is uncertain. The backend's existing per-center request ID replay and version CAS remain the single source of truth. No UI button can assert professional qualifications, authentic center-manager authority, payment, insurer contract, clinical access or public release.

## Real test acceptance

Chromium against the actual original Kestrel/PostgreSQL:
1. Intercept a real accepted POST after commit, drop only the browser ACK; UI must become uncertain; original center version 1/event count 1; read cannot unlock; manually retry **exact same bytes** gets `Replayed`, remains version 1 with one event.
2. Drop HTTP before server; manual same-command retry gets `Applied` once.
3. Drop original, let unrelated writer commit, then exact retry gets `409 StaleRevision`, no original event; show real current center.
4. Malformed/untrusted HTTP 200 cannot clear uncertain state; manual exact retry must use original bytes. Reload has no recovered commands or automatic POST. A supplementary Python real HTTP test replays the original UUID after later accepted center events and proves event count unchanged.
5. Retain existing 14 shared full regression jobs, center/member browsers, PostgreSQL journal, tamper/rollback, and Sprint92 test-to-CI census; green exact Push and Mainline PR CI then preliminary COMMENT review.

**Governance:** Synthetic test-only workflow; no real member/manager auth (TRN-TECH-012 remains NOT CODE-AUTHORIZED), licensure, clinic privileges, patient records, real services, finance, insurer, payment or Production readiness. All 17 national gates `BLOCKED_UNVERIFIED`; initial comprehensive national three-market release `DENIED_BY_DEFAULT`; completion `UNBASELINED`. PR must remain Draft/Open/Unmerged; no Ready/Merge/Stage/QA/Release/Production without product-owner instruction.
