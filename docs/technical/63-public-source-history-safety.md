# TRN-TECH-063 — Public Git source and reachable-history safety guard

**Date:** 2026-10-11. **Base:** Sprint53 PR #60, green exact-HEAD CI, Draft/Open/Unmerged. **Accepted policies:** TRN-BIZ-030/035/036/040/041; TRN-TECH-002 privacy gate T-03 and operational security gate T-08; backlog TRN-EP-01/02/06/11/12/13.

## Why Sprint54 is separate

The owner explicitly changed mahdimarzooghi4-debug/taranom from Private to Public to restore no-charge GitHub-hosted Actions. The repository and Git history are now visible to others. Green medical/financial synthetic CI cannot establish whether previously committed sensitive credentials, medical information or confidential documents have been exposed. Before adding more synthetic domain capability, establish a **fail-closed public-source guard**; this does not retroactively remove previously published content.

## Source, scope and limits

New stdlib-only Python policy script tools/policy_checks/check_public_source_history.py operates only on locally checked-out Git. It requires the exact top-level Git root and a complete, nonshallow checkout. It enumerates blobs reachable from locally fetched refs using git rev-list --objects --all and reads them through local git cat-file --batch. It rejects known sensitive tracked paths (.env except safe template names, private-key and credentials files), recognizable GitHub/AWS/Slack/Stripe-live/OpenAI-style keys and PEM private-key markers, and long literal token/password assignments. Long plain lowercase synthetic or test-only PostgreSQL Password fixtures are narrowly recognized only in tests source; live code, different field names, mixed/random-looking strings and high-confidence token signatures remain blocked. Even a removed file is detected while it remains reachable through Git history. Additional git-log historical filename checks catch sensitive path aliases that share identical Git blob content with harmlessly named files. Reusing the same suspected secret under another revision is NOT independently authorized. Oversized text sources are BLOCKED rather than silently skipped.

The checker **never prints or stores secret values or matching lines**: only rule names, source paths, abbreviated Git object prefixes and counts. It emits a machine-readable report and nonzero status on findings, Git unavailability, shallow history or other structural scan failure. PASS_LIMITED_SCOPE means ONLY that the sampled pattern/path rules did not flag locally fetched *reachable text Git blob objects*. It is NOT full historical source clearance. Binary contents, LFS, unreachable/dangling objects, commit messages, un-fetched refs, external caches/forks/clones, GitHub platform security alerts, true PHI classification, copyright/contract rights and already exposed material remain outside this check.

## CI and tests

27 Python unittest scenarios with temporary synthetic Git repositories verify a prior deleted secret still blocks, another fetched branch blocks, token/PAT/AWS/Slack/live-provider/PEM patterns, .env and credential paths, long literal secret detection, dummy-only credentials and safe .env.example, hash false-positive, lack of full history, non-git root and oversized text failure. The existing **12 pinned CI jobs** remain. In the member contract-test job, pinned checkout with persist-credentials:false and fetch-depth:0 is followed by 27 tests and a full-history scan **before** .NET build and inherited 981 member xUnit, real Postgres, built Chromium, no external AI and national DENY-ONLY checks. Redacted evidence attaches beside national blockers; do not add a thirteenth required job.

If the guard finds a possible leaked secret, fail closed. There is **no automatic deletion, history rewrite, token revocation, exposure-publication, bypass allowlist or credential rotation**; owner must privately investigate actual exposure, revoke/rotate affected credentials where appropriate, review related logs, and decide with security/legal assistance whether history rewrite is justified. Do not post the contents of a possible secret in PR comments.

## Release boundary

A PASS cannot authorize health data collection, funding, AI, live infrastructure, an official national launch or any real member/provider consent. Full first comprehensive nationwide launch across all three markets remains DENIED_BY_DEFAULT. No Ready, Merge, Stage, QA Gate, Release Approval or Production without explicit owner authorization and independent review.
