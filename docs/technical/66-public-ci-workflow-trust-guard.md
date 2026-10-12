# TRN-TECH-066 — Public GitHub Actions workflow trust fence

**Date:** 2026-10-11. **Source:** TRN-BIZ-030/035/036/040/041 → TRN-TECH-002 T-03/T-08 → TRN-TECH-063 public Git safety → TRN-TECH-064/065 private evidence custody → TRN-TECH-066. Scope is technical supply-chain security of the publicly visible repository's CI source; no public clinical, insurer, bank, research or Production claims.

## Risk and threat boundary

A successful credential *history* scan does not prevent a new PR from changing GitHub Actions to request write tokens, run privileged events, execute unreviewed marketplace actions or expose CI secrets. Many successive Sprint workflows inherit a large body of .NET/PostgreSQL/Chromium regression code; one unsafe workflow amendment could undermine all of those CI reports. Public PRs must remain unprivileged and no real private evidence must enter CI.

## Implemented static contract

The stdlib-only \`tools/policy_checks/check_public_ci_workflow_trust.py\` enumerates **all** current source \`.github/workflows/*.yml|yaml\` files (including the scanner's own workflow). It fails closed on missing/non-file/symlink/overlong/non-UTF8 workflow source, unexpected siblings, duplicate/missing mandatory roots, unknown trigger classes, non-read-only global permissions, job-specific privilege overrides, use of secrets/vars expressions, OIDC write authorization, unreviewed environment/container, self-hosted runners, dynamically referenced marketplace actions, external marketplace vendors and unpinned action references. Only the actual pinned SHA action set already inherited in Taranom's CI is permitted: checkout, setup-dotnet, setup-node, upload-artifact. Checkout steps must set \`persist-credentials: false\` **within the same step**, not a distant unrelated one.

This checker is a **bounded lexical policy** and not a general YAML parser, static taint analysis or proof of workflow runtime safety; indirect shell tricks, mutable runner images/PostgreSQL tags, transitive dependencies, network egress, hosted artifact retention and privileged settings in GitHub are outside its evidence. Findings contain **only** file paths and rule labels, never token contents. The machine-readable output always contains \`release_allowed=false\`, \`security_clearance=false\`. PASS means only that implemented invariants are satisfied on the checked source tree, not that PR review, Stage, security clearance or release is authorized.

## CI and acceptance

Inherited 12 CI jobs with .NET, isolated genuine test PostgreSQL, built Chromium, separate Git reachable-history guard, independently tested national DENIED policy and external AI prohibition remain. Insert workflow-trust test + static scan into existing internal-contract job **before** other build work, retain pinned checkout and \`persist-credentials:false\`, attach anonymized report. Unit tests include actual checked-in workflow tree and temporary malicious workflows (unpinned/third-party/dynamic action, credential persistence, write/OIDC permissions, privileged trigger, secret interpolation, self-hosted runner, environment/unknown YAML, symlink/missing root), no real tokens.

**Approval:** all 17 launch dimensions continue BLOCKED_UNVERIFIED and first official comprehensive national three-market launch DENIED_BY_DEFAULT. No Ready for Review, merge, Stage, QA, Release Approval or Production; preliminary technical COMMENT cannot replace independent security/human review.
