# TRN-TECH-136 — Sprint 127 organization technical workqueue

**Scope:** TRN-EP-01/02/12/13 bounded test-only enhancement stacked on Sprint 126; no new business status, authority, identity, real organization/user or external API.

## Coherent read model

A restricted PostgreSQL session_user selects the current fixed synthetic organization scope (SUPPORT for the localhost QA host). GET `/internal/demo/v1/org-draft-queue?afterItemRef=&limit=5` returns a bounded, C-collated, keyset-paged projection of the existing draft metadata and complete event history: `itemRef, sourceRef, activityCount, lastNoteRef, lastRequestRef`, and `nextAfterItemRef`. Unpopulated history has count 0 and both last references null. There is **no** domain status, approval, priority/risk inference, clock ordering, or staff identity.

The entire read uses **one REPEATABLE READ / READ ONLY PostgreSQL transaction**, verifies the existing immutable CREATE audit and source hash and **recomputes every chained activity hash** for each visible and lookahead item. Any missing/altered lineage fails the entire page closed. The queue does not materialize, mutate or duplicate the source journal. A later page is a new snapshot; there is deliberately no claim of stable multi-request snapshot continuity.

## Operator-facing synthetic QA

The Persian RTL React localhost-only center demo presents the scoped workqueue with activity counts and most recent opaque note refs, lets the human select a row, and explicitly re-reads the canonical source and history before writing. No implicit action or acceptance of `test:` as a real member/organization. Older draft list remains for compatibility. There is no browser tenant/role switch. The queue shares the existing fail-closed API envelope: `organizationAuthenticated=false`, `operatorAuthorized=false`, `nationalReleaseAllowed=false`.

## Acceptance

1. Real PostgreSQL 2-role isolation, sorted cursor + lookahead, empty and populated histories, exact source/hash re-attestation.
2. Tampered draft source fails the complete page; restoration reinstates deterministic result. 
3. Actual localhost HTTP query, cursor rejection, Authorization/cookie/tenant hints denial and React Chromium selection/refresh/forged envelope tests.
4. Inherited full 14-job CI remains green with all Python tests counted once; preliminary exact-HEAD Code Review recorded. Independent human approval pending.

No paid membership, clinical, insurer, sponsorship, enrollment, actual identity/permission, Stage/QA Gate/Release/Production or national official launch. `DENIED_BY_DEFAULT` remains mandatory.
