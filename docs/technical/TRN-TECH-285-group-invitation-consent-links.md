# TRN-TECH-285 — Group invitation links and independent response

Scope: extend owner planning drafts with manually shared, expiring invitation links and authenticated independent acceptance/decline. This records invitation consent only. It does not activate paid membership, create a payer relationship or grant wallet, appointment, clinical or dependent-account authority. Existing membership readiness remains inactive.

## Routes and data

- Owner `/patient-app/group-invitation?draft=<uuid>` is linked from each live draft. It loads only the JWT issuer/subject's draft and latest invitation. Owner actions explicitly create a link; nothing is automatically messaged to another person.
- `POST /api/v1/me/group-invitation-drafts/{draftRef}/invitations` accepts only a request UUID and a browser-generated 256-bit random lowercase hex secret. Seven-day expiry is an implementation link lifetime, not a membership period. An owner may create a replacement after unanswered, expired or declined invitations; accepted drafts cannot issue another invitation.
- `GET` at the same route returns the latest receipt, or the exact request receipt for reconciliation. It returns no raw secret or recipient account identity. The private planning label is not asserted to be the accepting person's verified identity.
- Recipient `/patient-app/join-group#<secret>` captures the secret only in component memory and clears the URL fragment. Authenticated `POST /api/v1/me/group-invitations/preview` reads status without accepting. `respond` requires an explicit Accepted/Declined decision and the exact `group-invitation-v1` consent version. Acceptance additionally requires a visible unchecked consent box in the UI.
- The recipient sees the invitation reference and status, not the owner's planning label, request UUID, profile, or another recipient's identity. Possession of the secret permits the first independent authenticated account to respond. This is a share-link flow, not official identity verification or a targeted member-directory inbox. Users must share only with the intended person.

## Persistence and concurrency

Only SHA-256 token hashes are stored. Secrets are never included in API URL/query strings, API responses or browser storage. A lost creation response reconciles through the owner/request UUID using the original in-memory secret. After closing the page, the owner can issue a replacement; the old unanswered token is invalidated. Losing a response on acceptance/decline is reconciled by preview under the same authenticated actor. If an exact read confirms no issue receipt, or recipient preview still shows Pending, an explicit retry resends the same request UUID/secret or the same decision. It cannot silently create a new invitation or duplicate acceptance.

All operations lock the draft before the invitation. Replacement invalidates prior pending links and inserts its own receipt atomically. Exact issue replays return the original current state and cannot revive a replaced or archived token. Archive and acceptance serialize on the same draft row; a draft archived before response makes pending invitations effectively Revoked. Already recorded acceptance/decline is retained as history. Expiry is enforced server-side, independent of the browser clock. The first recipient response binds the actor from trusted JWT issuer/subject; concurrent other actors receive 404. Same-actor same-decision retries retain the first timestamp; changing a final decision receives 409. Strict JSON prevents injected recipient identities or authoritative membership state.

## Validation

Browser cases exercise explicit issuance, fragment handling, no automatic acceptance, consent checkbox, decline/revocation, uncertain-write exact-read recovery, malformed receipt rejection, no-token network isolation, local shell assets, RTL and 390/320px widths. Real PostgreSQL HTTP tests exercise anonymous/cross-owner rejection, independent actor response, concurrent replay, replacement/archive revocation, non-resurrection, strict inputs, expiry, competing recipients and unchanged appointment/profile authority. The integration workflow includes both suites.

No APK rebuild, deployment, merge, real message or financial/clinical activation is part of this increment. Live identity login, targeted recipient verification, actual group/membership activation, withdrawal of active group relationships and production device testing remain separate work.
