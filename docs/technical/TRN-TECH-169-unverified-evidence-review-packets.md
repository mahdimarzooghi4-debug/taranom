# TRN-TECH-169 — DRAFT / Read-only unverified release evidence review packet
Date: 2026-10-17. This is a developer-loopback technical interface for human evidence gathering, not an approval or formal Code Authorization.

## Grounded sources
Reuse the *same* validated draft `national-launch-blockers.json` and `epic-capability-progress-draft.json` projection from TRN-TECH-165/166 and TRN-TECH-168. No new provider, human owner, acceptance criteria, business policy or provenance is invented. All 18 categories, 17 domains and 39 provisional capabilities remain unverified; national `DENIED_BY_DEFAULT`.

## New read-only contract
`GET /internal/ops/v1/dependencies/packet?requirement=CODE`, existing localhost host/forwarded-header fencing, no credentials, cache-control `no-store`, no write route. Only one *allowlisted* requirement code is accepted; absent/unknown/repeated/extra query parameters fail with 400 and deny fields. Unreadable or inconsistent source yields 503 and no candidate data.

Successful response is `TRN-TECH-169` / `DRAFT_UNVERIFIED_REVIEW_PACKET` with current source fingerprint, requirement, affected Epic and market/national domains, source-listed Business refs, candidate entries with unchanged evidence lanes, human/owner not observed, independent evidence false and Production/national false. Packet includes no personal, clinical or financial payload. It is a request-for-evidence information sheet, **not an independent cryptographic attestation**.

## Persian local operations
Selecting an unverified dependency loads a separate source-pinned packet. UI validates packet fields against the previously validated `/dependencies` snapshot including exact candidate and policy arrays. On failure, race, changed category or main source refresh loss, clear packet (and parent rows when unavailable), never show stale acceptance or implicit approval. No browser persistence or evidence upload.

## Mandatory checks
Read all 18 packets over real local .NET HTTP and compare against parent inventory. Negative malformed/query/POST/host tests. Real Chromium selection/reset and forged approval/fingerprint denial, and source refresh failure. Reuse full 14-job regression; CI or reviewer COMMENT never creates independent approval, Stage, QA Gate, Release or Production.
