# TRN-SP-57 — Public CI workflow supply-chain trust guard

**Date:** 2026-10-11. **Branch:** sprint-57-public-ci-workflow-trust-guard. **Base:** Sprint56 Draft PR #64, exact-head CI 12/12 Push and 12/12 PR, COMMENT-only review.

**Why this Sprint:** Real public repository risk not addressed by fake domain read models: the GitHub Actions definitions controlling CI can themselves be altered to gain credentials, permissions, run unpinned marketplace code or privileged event handling. Strengthen the existing CI trust boundary while waiting on real independent clinical/legal/infrastructure evidence. No service provider/API, contract approval, clinical rule or funding formula is invented.

**Deliverables:** TRN-TECH-066, standalone stdlib checker and temporary workflow negative/positive suite; require SHA-pinned and allowlisted Actions, explicitly read-only workflow tokens, credential-free checkout, nonprivileged events, hosted test runners, no direct secret expressions, no privileged environment or OIDC, fail closed for unsafe workflow file layouts. Scan all committed workflow source definitions including the new Sprint. Retain exactly twelve inherited CI jobs; run security suite in the existing contract job; append backlog evidence. An exit 0 is not security clearance nor legal release.

**Definition of technical completion:** tests + exact-head Push/PR 12-job CI both green, preliminary COMMENT-only Code Review, PR Draft/Open/unmerged. Independent human reviewer still required before any advanced gate. National three-market release always DENIED_BY_DEFAULT.
