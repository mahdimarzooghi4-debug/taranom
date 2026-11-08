# TRN-TECH-292 — Private owner notification after independent invitation response

Status: DRAFT technical contract, derived from TRN-BIZ-029/035 and existing TRN-TECH-281/284–291. No new membership, payer, clinical, recipient verification, external message or authority rule.

## Problem

An owner can review outgoing group invitations only by manually opening the invitation-history worklist. Reuse the existing authenticated in-app notification feed to notify the **issuer of the invitation** when a distinct authenticated recipient explicitly accepts or declines. A notification is an observation of consent, **not proof of membership or identity**. The private owner-entered label is not evidence of who answered.

## Source of truth and atomicity

The existing authenticated `POST /api/v1/me/group-invitations/respond` remains the only source of Accepted/Declined. Only a first successful transition from effective Pending to Accepted or Declined emits a notification, within **the same PostgreSQL transaction** as the state/actor/timestamp. Preview, invalid/expired/revoked requests, repeated same-actor final responses and unchanged pending links do not emit additional notifications. The invitation owner is always looked up from the persisted draft's issuer/subject, never from the recipient token or an API selector. Responses remain immutable on a later draft archive.

Add an internal, bounded canonical `taranom_member_notification_journal` with a unique (kind, source_ref), immutable source kind/ref, creation time, nullable first-read timestamp and global monotonic sequence. The earlier support-specific notification table remains an intake/legacy compatibility source. Startup under the existing bootstrap lock idempotently imports answered support rows preserving the first read timestamp, and existing final invitation answers using their original response times. New support and invitation events write the canonical journal transactionally. No background delivery, polling service, automatic sender message or external integration is introduced.

## Read and acknowledgment

Extend **existing** `GET /api/v1/me/notifications`, preserving its 20+1 sequence keyset, positive `before`, exact `notificationRef` lookup, no-store and one repeatable-read snapshot for page+global unread count. Authorization for each row is **re-derived from its persisted source at read time**: support request's issuer/subject for SupportAnswered; invitation draft's issuer/subject for GroupInvitationResponded. Missing/foreign source never appears. The response is an allowlist:
- SupportAnswered: existing `notificationRef, sourceRequestId, kind, createdAt, readAt` shape, unchanged.
- GroupInvitationResponded: `notificationRef, sourceInvitationRef, kind, responseState (Accepted | Declined), createdAt, readAt`. No respondent identity, request UUID, private label, link token/hash, consent actor, message, membership, finance or clinical record.
- Both kinds share the existing global `unreadCount` and `nextBefore` cursor.

`POST /api/v1/me/notifications/{notificationRef}/read` remains a deliberate, monotonic owner-only acknowledgment. The first read timestamp is retained on retry and cannot mark someone else's invitation or support response. No automatic acknowledgment on list/detail navigation.

## Browser

Keep the connected Figma 104:2 Persian RTL notification shell. Distinguish invitation Accepted vs Declined without asserting active membership or verified recipient identity. The invitation notification's detail link goes only to the **owner-scoped exact-reference** route `/patient-app/invitations-sent?invitation=<sourceInvitationRef>`, which independently rechecks authorization. Support links remain unchanged. No private data on a missing/denied session; malformed, duplicate or authoritative extra fields fail closed. No synthetic fallback. The existing owner-only response history remains the authoritative detailed record; notification is only a lightweight cue.

## Tests and gates

Real PostgreSQL HTTP tests: both final decisions, no events for Pending/Preview/Rejected/Revoked/Expired, concurrent same-actor retry exactly once, source-only owner isolation, anonymous/cross-owner read+ack denial, support+group shared global unread count and bounded cursor, no token/actor/label leakage, first-read timestamp stability, accepted state survives draft archive, and existing backfill support plus historical invitation backfill. Real Chromium: both card types, strict payload validation, exact owner deep-link, no membership claim, mixed unread/read, lost acknowledgment exact-read reconciliation, denial clearing, no-session no network, mobile RTL and 390/320px geometry.

Required on the **final exact SHA**: Figma Appointment Integration 2/2 and canonical 14-job mainline full regression, then a COMMENTED technical Code Review (not independent human approval). PR stacked Draft/Open above #295. No merge/Ready/Stage/formal QA Gate/Release Approval/Production. Nationwide comprehensive first release remains DENIED_BY_DEFAULT, with 17 blocked domains and 19 unverified external evidence categories.
