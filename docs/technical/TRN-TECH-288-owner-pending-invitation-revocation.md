# TRN-TECH-288 — Owner-initiated revocation of pending invitation links

Status: DRAFT technical implementation, scoped to the existing planning invitation lifecycle (TRN-TECH-284, TRN-TECH-285, TRN-TECH-287). Existing accepted `Revoked` state is reused, not a new membership rule.

## Purpose and hard boundaries

The authenticated owner who deliberately issued a still-pending, unexpired group invitation may stop that *one* link from being used without withdrawing the entire planning draft or creating a replacement invitation. This mitigates accidental link disclosure, with no irreversible modification to an independently recorded consent decision. There is no phone/email delivery, identity lookup, new paid membership, family relationship, wallet, medical/appointment authority or new provider grant.

A final Accepted or Declined response is **never** reversed, erased, treated as pending, or cancelled by an owner. A previously explicitly revoked link is an idempotent, immutable terminal state. Expired links are already unusable and stay Expired; archived drafts already make unanswered links effectively Revoked. A revoked link cannot be resurrected by replay of the old issuance request.

## Endpoint contract

`POST /api/v1/me/group-invitation-drafts/{draftRef:guid}/invitations/{invitationRef:guid}/revoke` requires the existing authenticated member JWT. Both IDs are nonzero. No user-controlled issuer/subject, recipient identity, decision, or payload fields are permitted. Empty body only. Owner's draft is located under trusted JWT issuer and subject and locked `FOR UPDATE` **before** reading/locking the invitation, matching existing issue/recipient-response/archive lock ordering. Unknown or foreign draft/invitation yields 404.

For the owner of an active Draft, a real `Pending` link with server-clock expiry in the future transitions atomically to existing `Revoked`. Exact retry on `Revoked` returns 200 and the same invitation reference and effective terminal state, `outcome: Replayed`; first transition returns 200 `outcome: Applied`. An already expired or final Accepted/Declined invitation returns 409 and stays unchanged. An archived draft does not grant another mutation; its unanswered links are already effectively Revoked. A race between owner revocation and recipient response serializes on the same draft row: exactly one outcome wins, with no post-acceptance revocation. Scope remains **one link**, not a blanket draft archive or different draft.

Response includes only existing owner invitation fields and `outcome`, with `Cache-Control: no-store`, no token/hash or recipient issuer/subject. State is durable in the existing invitation table, without creating another journal or endpoint to change the recipient's decision.

## Browser and ACK-loss discipline

The existing authenticated owner's `/patient-app/group-invitation?draft=<uuid>` page displays a manual two-step confirmation only when the latest invitation is Pending and the draft is still Draft. It explicitly warns that this stops the pending link but does **not** remove an accepted consent, change membership, withdraw the whole draft or send a message.

On an uncertain transport/ACK result, the client must hide any previously displayed reusable secret and retain only the **same draft and invitation references in component memory**. It reads exact owner activity via existing `GET /api/v1/me/group-invitation-activity?invitationRef=<uuid>` to reconcile. A confirmed Revoked state is success. A still-Pending exact record permits only a deliberate retry against **the same** revoke endpoint, never a fresh invitation issue. A terminal Accepted/Declined/Expired record remains non-revocable. No silent POST retry, storage persistence, forged ownership selector, or demo fallback. Unauthorized/invalid responses clear stale private cards.

## Required verification

- Real PostgreSQL HTTP: owner-only, anonymous/cross-owner denied, exact idempotent replay, token secrecy, no payload injection, response cannot resurrect via issue replay, competing recipient/revoker race, declined/accepted and expired never mutated, archive preserved, unrelated drafts unchanged, preexisting member appointment/profile scope unchanged.
- Chromium UI: manual explicit confirmation/cancel, pending-only control, lost POST ACK reconciled by **exact GET** without a duplicate POST, uncertain GET handling and deliberate same-command retry, final/expired/archived absence, missing session zero API traffic, malformed/forged receipt rejection, Persian RTL and 390/320px layout.
- Run existing Figma 2-job and canonical 14-job CI on exact candidate HEAD, record COMMENTED technical review, preserve stacked Draft/Open PR above #291. No merge, Stage/QA Gate, Release Approval or Production.

National launch stays DENIED_BY_DEFAULT. All 17 launch domains and 19 external evidence categories remain unresolved; no approved clinical/financial/identity integration is implied.

## Integration gate evidence (not release approval)

- Candidate content HEAD preceding this evidence-only commit: `8ef9afc7bedabba1168d57782f28fbfad7af2a93`.
- [Figma Appointment Integration #42490827932](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/42490827932): **2/2 SUCCESS**, exact backend PostgreSQL and real Chromium browser suites.
- The 14-job canonical regression must be repeated for the next exact SHA. PR #292 is temporarily based on `main` only to invoke this existing complete regression; restore the stacked base to #291 after the CI gate.
- Neither this evidence nor a green synthetic CI authorizes patient records, paid membership, real identity/consent verification, Merge, Stage, QA Gate, Release Approval or Production.
