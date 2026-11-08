# TRN-TECH-292 — Owner-scoped search of invitation planning drafts

Status: DRAFT. Builds on TRN-TECH-284/290 and the existing authenticated, owner-only planning drafts. This is a read-only discovery/filter increment, not a recipient directory or identity lookup.

## Contract

Extend only `GET /api/v1/me/group-invitation-drafts` with optional `q`, a 2–100 character trimmed text substring of the **owner's private, unverified recipientLabel**. An absent q retains the existing list exactly. For supplied q, require nonblank trimmed length 2–100; reject control characters, duplicate q selectors, q combined with exact reconciliation `requestId` or `draftRef`, and query parameter ambiguity. Keep the trusted JWT issuer/subject boundary before filtering. Filter by literal, case-insensitive PostgreSQL substring (`strpos(lower(recipient_label),lower(@q))>0`), not wildcard interpretation, before applying descending immutable draft sequence and 20+1 keyset pagination. No string-interpolated SQL. Unknown or other owners' private labels never appear; existing exact reconciliation and mutation receipts are unchanged. No new table/index is needed for a bounded owner worklist; no count or relevance score is inferred. Keep latestInvitation summary, allowlist and no-store unchanged.

## UI

On `/patient-app/group-members`, show a labeled text input and explicit Search and Clear actions; no request on keystroke. Submitting a valid trimmed term resets cursor, clears stale cards and asks the backend. Follow-on pages and refresh preserve the active search term; clearing restores the unfiltered list. Search is owner-only UI state, not URL, browser storage or backend write. Empty results explicitly say no draft matches, not that the recipient/account does not exist. On denied/malformed data, clear old cards. This search does not verify the person behind a label.

## Verification

Real PostgreSQL authenticated HTTP tests: a matching old draft buried under more than 20 nonmatches appears on page one, matching 22+ result keyset pages do not overlap, case-insensitive substring and literal percent/underscore semantics, withdrawn draft inclusion, foreign owner exclusion, duplicate/blank/short/long/control/conflicting selector denial, exact request/draft recovery preserved, unchanged latest summary and no-store. Real Chromium: explicit submit only, correct query/cursor/refresh/clear, empty result, denied refresh no stale data, no session/no traffic and narrow Persian RTL.

No new medical, financial, membership, identity, guardian, provider, messaging, notification or permission authority. National launch remains DENIED_BY_DEFAULT, with 17 unresolved domains and 19 unverified external evidence categories. Full exact-head Figma 2/2 and canonical 14/14 tests, technical COMMENTED review; stacked Draft PR above #295 with no merge or deploy.
