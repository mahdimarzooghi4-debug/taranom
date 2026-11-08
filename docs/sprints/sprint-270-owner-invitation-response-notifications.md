# Sprint 270 — Owner receives private in-app invitation response notification

Status: Draft / implementation candidate, stacked on PR #295. Based on TRN-TECH-292 and existing owner invitation consent and notification contracts.

## Outcome

An owner sees a durable, account-private in-app notification when an independent recipient has accepted or declined a previously issued group invitation; the notification opens that exact owner-scoped historical invitation. Existing support-answer notifications remain functional and present in the same paginated inbox.

## Implementation

1. Persist a canonical notification journal and idempotent startup backfill from existing support notifications and final invitation responses. Preserve existing support notification semantics and the global bounded keyset.
2. Atomically record a unique source notification only after an independent authenticated response first becomes final. No read or preview side effects. Preserve first response time and no duplicates on replay/concurrency.
3. Project both kinds through the existing authenticated notification read/read-ack endpoints, with source-derived owner checks, strict per-kind allowlists and one snapshot-consistent unread count.
4. Update typed member client and Persian RTL Figma notification cards; link only to exact owner invitation history, never expose respondent identity or label.
5. Verify with real PostgreSQL HTTP tests and Chromium positive/negative cases; run exact-head 2-job Figma and 14-job full regression and COMMENTED technical review.

## Non-goals / safety

No actual group membership activation, guardian/dependent management, subscriptions or wallet transfers, verified recipient identity, automatic invite sending, external email/SMS/push, medical/clinical authority, provider/insurer integration, new accepted Business rule or Production deployment. A response says only that the holder of a secret independently accepted/declined. No artificial member balance or entitlement is created. Retain national DENIED_BY_DEFAULT and all external gates.

## Admission

Keep Draft/Open/Unmerged on the intended stacked base #295. No Ready for Review, merge, Stage, formal QA Gate, release or Production without explicit owner approval. An isolated mainline gate may require temporarily retargeting this Draft PR to main and restoring its stacked base after exact-SHA green CI; no main commit or deployment occurs.
