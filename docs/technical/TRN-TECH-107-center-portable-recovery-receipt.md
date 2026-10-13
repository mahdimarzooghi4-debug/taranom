# TRN-TECH-107 — Portable manual recovery receipt for unresolved synthetic center command

**Date:** 2026-10-13 | **Sprint:** 98 | **Business:** accepted TRN-BIZ-053/054 and 028/030/040 | **Epics:** TRN-EP-04, TRN-EP-12 | **Parent:** Sprint97 Draft PR #108 exact `b07cdcb769782f21557ba42f0f7a376f736a5bde`, Push #38379745558 and PR #38379800005 both 14/14 green, initial COMMENT review, no independent human approval.

## Real usability problem and authority boundary

TRN-TECH-106 safely preserves the original command byte string and idempotency UUID only in the current browser page. Closing that page destroys the only record needed for exact manual replay if the database write committed but its HTTP acknowledgement was lost. A member-like login, identity token, persistent browser storage, automatic cross-device recovery or operating a real clinical center is NOT authorized.

## Narrow, explicit test-only solution

Offer **human-visible JSON export** while a command is uncertain. It is plain unsigned synthetic data and NOT a legal receipt or assertion of server commit. The entire envelope has exactly four ordered fields: `contract="TRN-TECH-107"`, `syntheticOnly=true`, original bounded `centerRef` and `originalBody` (the raw canonical serialized *five-field* TRN-TECH-073 original request). No clipboard reading, background sync, browser storage, URL parameter, cookie, signing key, external network, auto-export or automatic retry. User manually copies before leaving, then pastes after reload.

**Import is strictly non-mutating and distinct from replay.** Max 2400-character envelope, expected exactly four keys and canonical JSON encoding; originalBody at most 768 chars, parsed exact original five keys in canonical order, original valid v4 UUID, integer expectedVersion `0 <= n < Number.MAX_SAFE_INTEGER`, four preexisting command kinds, mandatory/null department/doctor relationships, synthetic ASCII `test:` refs only. No duplicate keys, unknown fields, role/rights flags, real clinical refs, alternate versions, whitespace-edited body or wrong scope. JSON is checked and reserialized for exact equality before any state change; failure shows denial and sends zero requests. This canonical/semantic check prevents accidental malformed command import, but **does not authenticate who authored the JSON**.

A valid import atomically restores the original scoped in-memory uncertain command and clears all former snapshots and historical views; locks new writes and center/department/doctor fields; no GET or POST on import. A separate deliberate click resubmits the **exact originalBody bytes**, endpoint and UUID. Existing PostgreSQL idempotency and optimistic CAS alone decide Applied/Replayed/Stale; the browser cannot infer any server outcome from receipt presence or a GET. If result is still ambiguous, the same original command remains pending. Any GET while uncertain must leave the actionable snapshot hidden, even if it succeeds. Closing or reloading without manually saving the receipt loses it; the app MUST NOT claim durable recovery.

## Acceptance

- Real Chromium/Kestrel/ephemeral PostgreSQL: accepted original write + lost browser ACK; manual export; reload; import causes **zero POSTs** and no unlock; explicit manual exact byte-for-byte replay returns Replayed with one event.
- Original blocked pre-server; same import/replay results in one Applied event. Distinct subsequent writer causes exact original stale denial. No business privileges.
- Forged authorization flags, real IDs, malicious extra/duplicate fields, tampered UUID/version/kind, noncanonical bodies, unrecognized versions and malformed JSON denied without POST or existing UI/context mutation; no browser local/session persistence, no command on reload.
- Existing center/postgres security, multi-center isolation, Member Web, 14-job full Push + Mainline PR CI and Sprint92 test census remain unchanged. Review exact final SHA as COMMENT only.

**Release:** All 17 national gates BLOCKED_UNVERIFIED; comprehensive first-release national three-market product DENIED_BY_DEFAULT; completion UNBASELINED; real member BFF TRN-TECH-012 NOT CODE-AUTHORIZED. No Stage/QA/Release/Production and no PR Ready/Merge without owner authorization.
