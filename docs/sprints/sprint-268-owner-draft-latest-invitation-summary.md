# Sprint 268 — Owner group-draft latest invitation summary

Status: Draft / implementation candidate. Stacked on PR #293. No merge, Stage or Production authorization.

## Goal

Make the existing owner group-draft worklist operationally informative without requiring the owner to open every draft. Each draft card shows the latest issued invitation's effective status and dates, or explicitly shows that no link has been issued.

## Contract

- Technical contract: TRN-TECH-290.
- Extend only GET /api/v1/me/group-invitation-drafts; keep the existing owner JWT issuer/subject boundary, 20+1 keyset pagination, no-store and all mutation receipts unchanged.
- latestInvitation is null or the five-field allowlisted latest invitation summary.
- The latest invitation is selected by immutable sequence, not by a preferred state. Effective Pending is derived against draft state and PostgreSQL clock.
- Use the existing (draft_ref, sequence_no DESC) index and a bounded lateral lookup. No new table, endpoint, invitation state, background worker or external send.
- An accepted invitation is not active membership, payment, wallet ownership, identity verification, guardian authority or clinical access.

## Implementation slices

1. Extend the draft-list SQL projection and typed API response with a strict latestInvitation summary. Keep POST create/withdraw DTOs separate.
2. Add latest-status/date presentation to each existing Persian RTL draft card. Preserve existing management and per-draft history links.
3. Add real PostgreSQL HTTP coverage for absent/latest/replaced/withdrawn/expired/accepted/declined states, ownership, privacy and pagination.
4. Add Chromium coverage for all status cards, no membership inference, no N+1 calls, denied refresh, malformed response, navigation and narrow RTL.
5. Run exact-head Figma integration (2/2), canonical full regression (14/14), then record COMMENTED technical review.

## Exclusions

No recipient directory, account lookup, targeted verification, phone/email/SMS, push notification, invite re-send automation, guardian/minor policy, active group membership, subscription, payer/wallet, medical record, patient access, or new business contract. No external identity, messaging, financial or clinical integration. No new authority or legal inference.

## Acceptance / handoff

PR remains Draft/Open and stacked above #293. Fix CI failures before adding unrelated work. Do not mark Ready, merge, run Stage/formal QA Gate, Release Approval, or Production without explicit owner instruction. National release remains DENIED_BY_DEFAULT; all 17 launch domains and 19 external evidence categories remain blocked/unverified.

## Verification checkpoint (not a release claim)

- Candidate implementation SHA before this record: `278a4dae980adfccde4ea15f7cabb090cf86f365`.
- Figma Appointment Integration [run 42494991585](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/42494991585): **SUCCESS 2/2**, including real PostgreSQL HTTP and Chromium member/center/admin coverage.
- This evidence-record commit intentionally changes the exact candidate SHA. Both Figma integration (2/2) and the full canonical mainline regression (14/14) must pass again on this commit before Code Review.
- The intended stacked base is PR #293. PR #294 is temporarily based on `main` only to invoke the existing 14-job regression, then must be restored to #293.
- No CI result authorizes merge, Stage, QA Gate, Release, real membership activation, financial/clinical authority or Production.
