# TRN-SP-160 — DRAFT / review packet for unresolved external evidence
- Stacked over Sprint 159 Draft/Open PR #173, exact base `cb9efbb5500fd8baa3b711cfcf27a47c9c43e182`.
- Technical slice: [TRN-TECH-169](../technical/TRN-TECH-169-unverified-evidence-review-packets.md).
- Purpose: a new read-only localhost .NET packet route and Persian React view to hand reviewers the exact source-backed refs/capabilities for any of the 18 unresolved requirement categories, without fabricating a human owner or review decision.
- Tested boundary: all 18 packet responses, strict query shape and denied mutation; browser validates source fingerprint + cross-projection data and discards forged/stale packets.
- Not included: upload, external evidence handling, credentialed source trust, independent verification, approval, operational clinical/financial/identity integrations, production AI bridge, merge or deployment. National release stays `DENIED_BY_DEFAULT`.
- Acceptance gate: same full 14-job CI, then exact-head technical Code Review COMMENT; PR remains Draft/Open/Unmerged.
