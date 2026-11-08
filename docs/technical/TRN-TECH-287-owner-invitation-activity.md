# TRN-TECH-287 — Owner-scoped invitation activity

Status: DRAFT technical implementation contract. Derived from accepted TRN-BIZ-029/035 and existing TRN-TECH-284/285/286. No new membership rule, recipient directory, automatic message or entitlement.

## Purpose and boundaries

The owner who manually issued planning-group invitations can revisit **all their past invitations**, including older replaced links, to see independent recipient responses. The existing owner detail displays only the latest invitation in a draft; the recipient's separate response history must remain recipient-only. Each invitation is only a consent record, not active membership, a household relationship, identity verification, a payment or access grant.

The five existing effective states are Pending / Accepted / Declined / Revoked / Expired. Expiry uses the server clock; a withdrawn draft revokes only previously unanswered invitations. An answered invitation retains its original final state even after draft archive. The owner's private planning label does not verify the person who responded.

## API

`GET /api/v1/me/group-invitation-activity` requires the existing authenticated member JWT and derives owner exclusively from trusted issuer/subject. Optional `invitationRef` selects one owner-owned invitation, otherwise returns an empty list. Optional positive `before` keyset cursor pages descending sequence. These filters are mutually exclusive. Unknown/duplicate query keys, zero UUIDs or nonpositive cursors fail 400.

Each result contains only `invitationRef`, `draftRef`, `recipientLabel` (private owner-supplied planning label), `draftState` (Draft/Withdrawn), `state`, `createdAt`, `expiresAt`, `respondedAt`. No token, token hash, request UUID, recipient login identity, reviewer detail or external authority. The first 20 records form a page; row 21 only establishes `nextBefore`. Read via existing invitation/draft storage and indexes. Response: `Cache-Control: no-store`. No mutation or new table.

## Browser

`/patient-app/invitations-sent` is the connected, Persian RTL, owner-only activity view in the existing patient shell. It exposes latest effective status, owner label, response time when final, manual refresh, older records and a link to the owner draft. Account/group-drafts navigation links to it. No browser storage, secret, recipient identity or automatic message. On missing session, denied response or malformed data the view clears private rows, with no sample fallback. Typed receipts reject duplicate references, invalid states, extraneous authoritative fields, nonmatching exact lookup and malformed cursors.

## Tests and governance

Real PostgreSQL HTTP tests exercise owner isolation, anonymous/cross-owner denial, history after archive, replaced/expired/declined/accepted status, 22-record nonoverlapping pagination, exact private lookup and malformed requests. Chromium tests cover the same read-only UI, 390/320px layout, no-session no traffic, denied refresh clearing and malformed receipts. Run Figma integration plus the 14-job regression at exact head, then record technical COMMENTED review. Keep this work stacked Draft/Open above PR #290; do not merge or deploy.

National release still DENIED_BY_DEFAULT: 17 release domains blocked; 19 outside evidence categories unverified. No Stage or Product Release acceptance follows from CI.
