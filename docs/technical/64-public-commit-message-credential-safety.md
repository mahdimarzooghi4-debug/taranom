# TRN-TECH-064 — Public commit-message credential exposure fence

**Date:** 2026-10-11. **Base:** TRN-TECH-063 / Sprint54 PR #61 Draft/Open, exact-head CI green and preliminary Code Review COMMENT. **Accepted authorities:** TRN-BIZ-030/035/036/040/041; TRN-TECH-002 T-03 privacy and T-08 security/release. **No real patient data, passwords, clinical integrations, consent, financing, or deployment.**

## Problem and boundary

The repository is now Public by explicit owner action. Sprint54 protects path names and locally fetched reachable Git *blob* contents, including some historical deleted files. It explicitly does **not** inspect Git commit messages. Commit subjects/bodies may carry a credential-looking string independently of any source blob and are routinely publicly visible even if a change is reverted.

This Sprint adds a narrowly scoped, offline defensive check for **messages of commits reachable from locally fetched refs**, WITHOUT reading GitHub secrets or mutating history and WITHOUT creating actual credentials. The scanner continues to state `PASS_LIMITED_SCOPE`, never `SAFE_FOR_PUBLICATION`; independent credential/legal/privacy review remains open.

## Exact check

Extend `tools/policy_checks/check_public_source_history.py` with an offline `git log --all --format=%H%x00%B%x00` operation. Parse only structurally valid NUL-delimited commit ID/body pairs (a Git commit message cannot contain NUL); guard against missing, malformed and oversized (10 MB) metadata stream; count every examined commit. Reuse the existing high-confidence GitHub/AWS/Stripe/Slack/OpenAI-style/private-key patterns and long literal credential assignment detector for the **message body**, with no test-file password exemption. Emit `commit_messages_checked` in the existing DENY-ONLY JSON alongside prior fetched reachable blob counts. Findings are only `rule`, fixed string `commit-message`, abbreviated commit SHA and count. Never print the commit subject/body or matched secret/token. Any match, scan failure, malformed framing or scan omission fails closed.

All existing blob scans, historical path-alias controls, synthetic credential fixture narrow exception **only on test file blobs**, release DENY-ONLY, absent real integrations and build regressions continue unchanged. No scanning of unreachable/dangling Git objects or unfetched remote refs; GitHub-hosted secret alerts and binary/LFS remain outside scope.

## Tests and reproducible CI

Add **six** isolated synthetic Git unit cases: high-confidence token in a previous commit message with report redaction; long literal password in body; suspicious commit on another fetched branch; benign normal commit; abbreviated commit identity with no raw token; and verification that even a synthetic-looking `Password` in a commit message is not exempted. Total security unit tests **33** (previous 27 + 6). Existing pinned 12-job CI workflow `.github/workflows/sprint-55-commit-message-credential-safety.yml` uses full checkout in the same internal-contract job, executes the 33 tests and both blob + message checks, publishes redacted JSON, then runs all inherited .NET, actual isolated PostgreSQL, built Chromium, external AI exclusion and national release refusal tests. Do not add a new job or network check.

## Governance/limits

An incident finding requires owner-led private investigation and revocation/rotation where appropriate. Never auto-rewrite Git history, change repository visibility, expose captured values, infer actual legal rights to publish, or whitelist a confirmed secret. A clean limited detector is NOT proof that history/public branches contain no real member personal/medical or commercially confidential content. The comprehensive **national first official release** across individual, support, corporate remains `DENIED_BY_DEFAULT`. All stacked PRs Draft/Open/Unmerged; no Ready, Merge, Stage, QA Gate, Release Approval or Production without explicit instruction.
