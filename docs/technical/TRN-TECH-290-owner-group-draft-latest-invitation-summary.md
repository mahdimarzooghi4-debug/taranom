# TRN-TECH-290 — Latest invitation status in the owner group-draft worklist

Status: DRAFT technical scope derived from the existing accepted owner draft and invitation contracts TRN-TECH-284 through TRN-TECH-289. Read-only projection of existing records. No new invitation state, consent, membership, recipient identity, notification, delivery or payment rule.

## User problem

The owner group-draft list currently shows only the private planning label, draft state and creation date. To know whether an invitation was ever issued, is still usable, was answered, expired, revoked or replaced, the owner must open each draft separately. The existing per-draft invitation history and pending worklist already provide authoritative detail; the draft list needs a compact, truthful summary to help the owner choose which existing workflow to open.

## API extension

Extend only the response of the existing authenticated GET /api/v1/me/group-invitation-drafts read. Keep the owner issuer/subject filter, descending draft sequence, page size 20 and 21st-row cursor sentinel unchanged.

Each returned draft includes latestInvitation:

- null when no invitation has ever been issued for the draft.
- Otherwise exactly invitationRef, state, createdAt, expiresAt, and respondedAt.
- Select the row with the greatest immutable invitation sequence for that draft, regardless of its terminal state. This is the latest issued link, not "latest pending".
- Derive effective state with the same server-clock rule as the existing owner/recipient history: persisted Pending on a Withdrawn draft is Revoked; persisted Pending whose expires_at <= clock_timestamp() is Expired; otherwise retain the persisted Accepted/Declined/Revoked/Pending state. Never treat a withdrawn or expired link as usable.
- Use a bounded LEFT JOIN LATERAL over the existing (draft_ref, sequence_no DESC) index. No new table, endpoint, client-side N+1 request, or unbounded per-draft history scan.
- The summary allowlist excludes request IDs, token/token hash, recipient issuer/subject, consent actor, identity evidence and all clinical, financial or membership authority. Parent draft's existing private planning label remains an owner-only, unverified label.
- Existing POST create/withdraw receipts remain the existing draft DTO; invitation issuance, response, revocation and exact ACK-reconciliation APIs are unchanged. No write or notification occurs during list reads.

## Browser

On /patient-app/group-members, each draft card shows either "No invitation issued" or the latest link state and its creation/expiry/response time where present. Use explicit Persian labels for Pending, Accepted, Declined, Revoked and Expired. An Accepted invitation is explicitly described as an accepted invitation only, not an activated group membership. The summary is a read-only cue; existing "Manage invitation" and per-draft history links remain the action/detail routes. No invitation secret is ever rendered in the list.

## Verification

Real PostgreSQL HTTP tests: no invitation yields null; multiple replacements select the highest invitation sequence; a terminal latest invitation is not replaced by an older Pending link; withdrawn and server-expired Pending states derive Revoked/Expired; accepted/declined timestamps and states remain immutable; owner isolation, no-store, privacy allowlist and unchanged 20+1 draft keyset. Existing draft creation/withdrawal replay, invitation history, Pending worklist and revocation tests remain green.

Real Chromium tests: summary displays no-link and each effective state, expiry/response dates, no membership claim on Accepted, per-draft management/history navigation remains correct, forged/extra summary fields clear stale data, denied refresh clears prior cards, missing session sends no private API request, RTL and 390/320px layout. No N+1 invitation requests.

Require exact-head Figma integration 2/2 and full mainline regression 14/14 before recording a COMMENTED technical review. Stack Draft/Open above PR #293. No merge, Ready for Review, Stage, formal QA Gate, Release Approval or Production. National launch remains DENIED_BY_DEFAULT; 17 domains and 19 external evidence dependencies remain unresolved.
