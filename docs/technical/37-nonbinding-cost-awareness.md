# TRN-TECH-037 — Nonbinding, source-explicit pre-treatment cost awareness

**Date:** 2026-10-10. **Base:** Sprint27 Draft PR #34. Business authority: **TRN-BIZ-050** unified *nonofficial* view of final provider/insurer/credit/member amounts, **TRN-BIZ-051** clearly nonbinding pre-treatment cost preview with source and unknown visibility, and **TRN-BIZ-052** member's awareness/choice recorded separately from real clinical consent or legally enforceable financial obligation. All real sources, lawful use, regulatory actor, currency and accounting formulas remain unselected/unverified.

## Product problem

The disconnected Member Web has a generic 'برآورد هزینه' page but no visible distinction among **five separate shares** and no test contract preventing a future missing insurance amount from silently becoming zero or a claimed estimate from being presented as an official final bill. It also lacks a technical, version-pinned, nonbinding member-choice record independent of payments, clinic consent and wallet activity.

## New internal .NET boundary: source-explicit synthetic preview

`SyntheticCostPreviewBuilder` is an **internal pure .NET** test foundation, no actual member or source authority and no public API. Requires one `test:` scoped member, nonempty preview UUID, positive revision and **exactly one** typed component each: provider service estimate, primary insurance, supplemental insurance, treatment credit and member remainder. Reject duplicates/missing kinds/unknown enum values.

Every component has an **explicit evidence state**: `Missing` (no amount, currency, source, evidence ID or revision), `ProvisionalClaim` (nonnegative caller-claimed decimal including **literal zero**, caller-supplied three-letter uppercase currency, synthetic source ref, nonempty evidence UUID and positive source revision), or `ConflictingClaims` (no numeric amount/currency, but synthetic source/evidence lineage and revision). The reducer does **not** mark anything verified; it is a validity filter for untrusted test fixtures only. Provisional monetary units must agree across the supplied components, otherwise reject. **No total calculation, allocation, offset, currency exchange, missing-value guessing, inferred wallet balance, price accuracy or insurance entitlement** is performed. No default amount or policy rate.

The aggregate result is exclusively `Invalid`, `MissingOrIncomplete`, `Conflicted`, or `AllComponentsProvisionalOnly`. Even the latter is **not** official, authorized, final, affordable or payable. A SHA-256 fingerprint derived from length-delimited canonical `test:` fields, independently of list order, pins revision and all component claims; it is *not* a legal document hash/signature or tamper resistance against the record/source owner. A conflicting component overrides missing in the display status but both still preserve unknown amounts.

## Separate test-only choice history

`CostAwarenessRecorder` is a pure, **in-memory only** revisioned recorder. An initial snapshot is created from a non-invalid synthetic cost preview and stores exact member, preview ID, preview revision and fingerprint. A `CostAwarenessCommand` pins those exact values and carries one of three **nonbinding** synthetic responses: acknowledged uncertainty, deferred, or requested clarification. Exact historical replay is idempotent even after newer choices. Modified replay, stale expected revision, changed preview revision/fingerprint or cross-member/preview attempts are rejected. None of the outcomes means treatment consent, financial liability, payment, credit reservation, eligibility, acceptance or a valid authenticated user signature.

There is **no DB, BFF, HTTP route, durable audit, actual session or member identity provider** for this cost recorder, and it must not be connected to real records or used to infer obligations. When a legitimate provider/insurer source or approved disclosure version changes, a future authorized integrated product must use its own security/identity/re-consent governance before any real choice can be recorded. The test-only command is not proof that a real member saw a UI.

## Disconnected Persian RTL Member Web

The existing `/member/costs` route now displays five accessible labeled sections, each **'اطلاعات موجود نیست'**, not `0` or a made-up illustrative rial amount. Each explains the expected source category and unavailable official documents, and the whole view clearly states that it does not guarantee price, coverage, credit or member liability. The disabled **'ثبت آگاهی و انتخاب — نیازمند اتصال امن'** control is purely explanatory and MUST stay disabled until legitimate access/consent/security/contract integration is designed. The built shell retains `connect-src 'none'`, no member login, no external API, no localStorage/sessionStorage mutation, no fake data or payment.

## Acceptance, limitations and gates

- Real isolated xUnit tests for all five component states, zero-vs-unknown, explicit conflicting amounts, cross-currency rejection, unique components, canonical fingerprint stability/version/source binding, malformed refs, changed estimate, replay/stale/cross-member nonbinding choice history, and absence of financial decision vocabulary.
- Real built Chromium Playwright checks for five separate visibly unknown sources and disabled choice, zero private requests/no payment action, and navigating/Back/Forward. Preserve all inherited Chromium, MemberGovernance, PostgreSQL and DENY-ONLY controls in **twelve independent CI jobs**.
- **No real rate, currency adoption, quote, provider, policy, insurer, member identity, legal medical consent, wallet, reservation, billing, source evidence or authoritative API** has been selected. Real financial settlement/order of payment and insurance authorization still require separate contracts. Medical emergencies must not be delayed for price disclosure.
- Preserve Draft/Open/Unmerged. No Ready/Merge/Stage/QA Gate/Release/Production or claim of independent legal/financial/clinical/security approval.
