# TRN-SP-03 — Member Identity and Read Integration Readiness (Planning Candidate)

**Date:** 2026-10-10  
**Status:** PLANNING / DRAFT / **NO SPRINT 03 CODE AUTHORIZATION**.  
**Related:** `TRN-EP-01/02/03/06/07/08/09/10/13`, [TRN-TECH-012](../technical/12-member-session-and-governed-read-boundary-candidate.md), [mandatory Member Web PR #5](https://github.com/mahdimarzooghi4-debug/taranom/pull/5).

## Objective

Enable eventual secure connection of the already-coded RTL member shell to **actual .NET-owned member identity and versioned authoritative read models**, without ever equating green synthetic CI with real identity, credit, clinical/legal consent or insurance.

**Why Sprint 03 is not yet Code-ready:** no selected trusted real IdP/member proofing path; no reviewed identity-to-member mapping, BFF/session/CSRF topology, production-grade permissions, legal clinical data/consent authority, real finance/provider/insurer source or concrete .NET read endpoint contracts. The prior Sprint 01 synthetic principal and Sprint 02 disconnected frontend explicitly do not solve these.

## Candidate logical packages (planning / no code under this PR)

| Package | Deliverable to review before Code | Gate / dependency |
|---|---|---|
| SP03-01 — Member identity authority | Real identity verification evidence, subject/member/tenant mapping, sponsor restrictions, owner and legal authority | Verified IdP/provider and Member/Identity governance agreement |
| SP03-02 — Session security boundary | Chosen .NET-owned or trusted BFF architecture, same-origin/CSRF/session/revocation/error/security design | Hosting/identity/trust/session contracts and independent security review |
| SP03-03 — Read-model registry | Domain-by-domain field/provenance/schema permission matrix for nine Member Web routes | Each bounded-context owner's approved technical contract |
| SP03-04 — Negative integration contract tests | INT-01…INT-12 failures/denials, no fake data or cross-org leakage | Actual reviewed seams + dedicated synthetic test isolated from real identity |
| SP03-05 — Carefully bounded code slice | ONLY after explicit separate Code Authorization; cannot include real wallet, clinical, insurer or provider behavior without their approvals | Approved .NET/React interface contracts, CI, Stage prohibition remains |
| SP03-06 — Independent Code Review handoff | Findings and retesting for current PRs #3/#5, human sign-off | [GitHub issue #6](https://github.com/mahdimarzooghi4-debug/taranom/issues/6) |

## Ordered dependency path

`Independent Code Review and accepted open findings` → `real IdP/member mapping and trust model` → `selected session/BFF topology` → `owned authenticated read contracts` → `explicit Sprint 03 Code Authorization` → `implementation/tests/green CI` → `Code Review` → `Stage only with explicit later permission`.

Read-model work may be prepared **in parallel** with obtaining independent Code Review, but passing one does not automatically waive the other. Until both domains and authorization are ready, **keep the current disconnected member shell**. No dummy identity, fabricated credit and no invented endpoints.

## Definition of Code-ready for first genuine integration slice

- A real authorized identity source has been selected and validated (not a synthetic placeholder), member identity proofing and subject-to-member mapping governance approved.
- A signed-off server trust boundary exists, with documented authentication/authorization session/CSRF/revocation and tested fail-closed behavior for denials/unavailable sources.
- The exact first read model is chosen from an actual .NET domain with verified data authority; field set, version, tenant and legal purpose defined. **Do not choose or activate a live sensitive route by convenience.**
- INT test matrix is tied to executable integration fixtures and CI. No real health/finance/identity data in CI logs.
- Separate user Code Authorization expressly names the bounded implementation package and prohibits Merge/Stage/Production without later approval.

## Present status and blockers

- Sprint 01 [PR #3](https://github.com/mahdimarzooghi4-debug/taranom/pull/3): synthetic .NET foundation, green CI, **independent Code Review missing**.
- Sprint 02 [PR #5](https://github.com/mahdimarzooghi4-debug/taranom/pull/5): 9-route React member shell, **16 production-bundle Chromium tests green**, **independent Code Review missing**.
- Planning [PR #4](https://github.com/mahdimarzooghi4-debug/taranom/pull/4) and Business/Technical PRs #1/#2 still Draft/Open/unmerged.
- **Never select a vendor, login flow, domain read endpoint, financial amount, clinical data, service integration, session secret or compliance retention rule from assumptions.**
- No Stage/QA Gate/Release/Production. No public national rollout from a partial development Sprint.

**Outcome:** Sprint 03 is now explicitly ready for **contract review/planning**, **not for Code**. The next actual work depends on verified external IdP/contract decisions and independent reviewer assignment; issue #6 remains OPEN.
