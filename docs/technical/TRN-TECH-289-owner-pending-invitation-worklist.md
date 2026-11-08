# TRN-TECH-289 — Owner pending invitation worklist and per-draft history

Status: DRAFT technical scope, derived from already-accepted independent group-consent and owner-issued invitation contracts (TRN-TECH-284/285/286/287/288). This is an owner-facing **read-only filtered view of existing records**. It does not add membership, guardian authority, payment, a new state, a delivery channel, or automatic notifications.

## User need and failure mode

The owner activity screen is currently a newest-first, 20-row page of every past link. With enough Declined/Revoked/Expired history, an older, still-Pending link for another draft can be buried beyond page one. Since the owner-only pending-link revocation from TRN-TECH-288 is initiated from that activity page, that creates a material discoverability problem. The owner also needs a per-draft history without conflating planning labels with independently verified recipient identities.

## Read API extension

Extend only existing `GET /api/v1/me/group-invitation-activity`, retaining the trusted issuer+subject owner filter and eight-field allowlist.

- `?state=Pending` selects **current effective Pending**, not merely a persisted `Pending` row: only invitation.state Pending, draft.state Draft and invitation.expires_at later than the PostgreSQL server clock. No other state value, readiness/score or "active member" value is accepted.
- `?draftRef=<uuid>` selects the owner's exact planning draft, including its replaced, answered, revoked or expired links. Foreign/unknown draft yields empty results (no existence disclosure). Both `state=Pending` and `draftRef` may be combined with a positive `before` keyset cursor; filtering takes place **in PostgreSQL before LIMIT 21**, never by filtering the first 20 results.
- `?invitationRef=<uuid>` remains exact lookup for existing lost-ACK read reconciliation. It cannot combine with `state`, `draftRef` or `before`. Duplicate/unknown query keys, empty values, zero UUID or nonpositive cursor are 400. No query causes writes.
- Sorting remains descending immutable invitation sequence; page size is 20 with row 21 only to signal `nextBefore`. All responses remain no-store and exclude secret, token hash, request ID, respondent issuer/subject, verified identity and any authority claims.
- Existing owner activity exact-reference calls, recipient history, link issue/revoke and consent mutations are unchanged.

## Browser

The existing `/patient-app/invitations-sent` screen offers manual `همه دعوت‌ها` vs `در انتظار پاسخ` modes. The filter is a **server request** and changing it resets the pagination cursor and clears old private rows before fetching. The state of the filter is in React memory, not browser storage; the "Pending" worklist can expose a link's existing explicit revoke confirmation, with its exact-reference GET and replay protection from TRN-TECH-288 still intact.

Deep-link `/patient-app/invitations-sent?draft=<uuid>` shows only activity belonging to that draft, with manual All/Pending filter and a route back to all outgoing invitations. The draft list's cards link to their corresponding owner-scoped history; no acceptance is presented as active membership. Invalid or conflicting `invitation`/`draft` parameters fail locally without API access. A remote denied or malformed response must clear stale private activity, not render historical demo data. Server-provided rows in the selected draft must match that draft; Pending-filter rows must carry valid Pending state with Draft state. Only the original exact invitation read is allowed to resolve an uncertain revocation, not a filtered page.

## Verification / gates

Real PostgreSQL HTTP regression: many nonpending rows newer than one old pending link; Pending finds that old link on page one; combined draft+Pending, 22+ pagination, exact foreign draft absence, archived/expired exclusion, duplicate/unknown/conflicting filter denial, no wrong-owner leaks, old unfiltered and exact lookup preserved, and no new permission/activation.

Real Chromium: switching modes resets cursor without client-side fake filtering; an older pending link becomes visible/actionable; per-draft link and filter work; malformed/foreign responses, no-session/no traffic, denied refresh and 390/320px RTL layout; revocation ACK-loss reconciliation remains exact even when filtered.

Exact-head Figma integration 2/2 and full regression 14/14 are necessary to record COMMENTED review. PR remains Draft/Open stacked on #292. No merge/Stage/formal QA Gate/Release/Production; nationwide launch is DENIED_BY_DEFAULT (17 domains and 19 external evidence categories still unresolved).

## Exact-head integration evidence (not a release claim)

- Feature candidate HEAD before this evidence-only commit: `2990af5a3f78ca61fbef71ed12f6c3ff2ad02660`.
- [Figma Appointment Integration #42492971086](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/42492971086): **SUCCESS 2/2** (real PostgreSQL HTTP contracts and Chromium member/center/admin suites, including owner Pending worklist).
- The entire 14-job canonical mainline regression is required **on the new exact SHA** of this document commit. PR #293 was temporarily retargeted to `main` only to invoke the existing regression; restore the intended stacked base to #292 after both tests are fully green.
- This document and CI cannot approve actual memberships, real identity, payments, medical services, release or Production.
