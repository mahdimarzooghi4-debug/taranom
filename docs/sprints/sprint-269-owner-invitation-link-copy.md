# Sprint 269 — Explicit copy action for owner invitation links

Status: Draft / implementation candidate. Stacked on PR #294. No merge, Stage or Production authorization.

## Goal

Complete the existing manual group-invitation share experience: an owner who has deliberately issued a Pending invitation can copy the exact displayed link with one explicit click, with a safe manual-selection fallback when clipboard permission is unavailable.

## Contract

- Technical contract: TRN-TECH-291, derived from the existing TRN-TECH-285 manual link sharing policy.
- No backend or database change. Keep the 256-bit random secret, fragment-only URL, seven-day expiry, server-side hash, independent recipient consent and all exact ACK recovery behavior unchanged.
- Clipboard write occurs only from the explicit owner click. Never read clipboard contents.
- On clipboard failure, retain the link and select its full textarea for manual copying; announce fallback accessibly.
- No auto-send, SMS/email/push, contacts, share-target integration, background copy, browser storage, clipboard polling or secret telemetry.

## Implementation slices

1. Add an owner-only copy button and live status to the existing invitation link field.
2. Add safe fallback focus/selection on clipboard denial or missing API.
3. Add Chromium tests for click-only clipboard writes, exact link content, denied permission fallback, no extra API mutations, replacement status reset and existing narrow RTL behavior.
4. Run Figma Appointment Integration 2/2 and full canonical mainline regression 14/14 on exact HEAD; then record COMMENTED technical review.

## Exclusions

No new invitation lifecycle/state, membership activation, guardian/minor policy, verified recipient identity, recipient directory, automatic message delivery, contact discovery, subscription/payer/wallet, clinical or health-record access, or external integration. No new business/legal/clinical contract.

## Acceptance / handoff

PR remains Draft/Open/Unmerged and stacked on #294. Fix CI failures before feature work. No Ready for Review, merge, Stage/formal QA Gate, Release Approval or Production without explicit owner instruction. National launch remains DENIED_BY_DEFAULT; all 17 domains and 19 external evidence categories remain blocked/unverified.

## Exact-head verification checkpoint (not release approval)

- Candidate implementation SHA before this evidence-only commit: `683609481b28575e7cb9e6258b036842b34653ba`.
- Figma Appointment Integration [run 42496607576](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/42496607576): **SUCCESS 2/2**, including member Chromium suites and real PostgreSQL HTTP contract suites.
- This checkpoint changes the candidate SHA; the canonical 14-job full regression must pass on the resulting exact HEAD before technical review.
- PR #295 is temporarily based on `main` solely to trigger the canonical `Taranom Mainline Full Regression`; after the exact-head 14/14 result it must be restored to its intended stacked base `feat/owner-group-draft-latest-invitation-summary` (#294).
- This CI evidence is not a Stage, formal QA Gate, Release Approval, Production, external messaging, or membership activation.
