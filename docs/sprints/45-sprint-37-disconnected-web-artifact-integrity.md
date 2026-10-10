# TRN-SP-37 — Disconnected web build output security and SHA-256 evidence

**Date:** 2026-10-11. **Base:** Sprint36 `sprint-36-national-launch-blocker-evidence-gate`, PR #43 Draft/Open.

Business TRN-BIZ-030/035/036/037/040 → TRN-TECH-046 → EP-12 Operational Security and EP-13 Member Web → Sprint37 → artifact checker → negative tests → actual bundles/Chromium → CI → preliminary review.

- **BL-370 — Artifact manifest:** Canonical per-application SHA-256 of all built local files and full inventory, deterministic order, verified from bytes on second pass. No digital signature or external trust claims; `release_allowed:false`.
- **BL-371 — Closed static resource set:** HTMLParser and CSS active resources may only reference packaged local hashed assets; deny remote script/CSS loads, inline scripts, forms/frames/redirects, missing output, sourcemaps, symlinks, corrupt text, out-of-tree refs and unapproved file types.
- **BL-372 — Adversarial checks:** 27 standalone Python-stdlib tests for denial and exact digest roundtrip, mutation/tamper, cross-surface reuse, forged release status and evidence locality. Two additional built Chromium tests confirm actual only-local fetched route + JS/CSS requests.
- **BL-373 — CI:** Inherit **12 independent** Sprint36 jobs; invoke scanner and unit matrix from both `npm run build` jobs before browser tests, emit hashed manifest attached to each job. Preserve source-only external AI denial, built HTTP frame denial, release blocker registry DENIED, clinical protection DENY-ONLY, actual isolated PG and all previous tests.
- **BL-374 — Review:** Source-level Code Review as COMMENT and exact-head green Push/PR CI required. Independent security/legal/clinical/financial reviewers are NOT assumed. All external real-world product and national launch evidence blockers remain.

No real identities, patient records, clinical provider/insurer agreements, real member deposits, operating model, AI inference/training, external endpoints or Production hosting created. No Ready, Merge, Stage, QA, Release or Production.
