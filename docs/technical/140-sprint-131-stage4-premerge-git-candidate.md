# TRN-TECH-140 — Sprint 131 pre-Stage4 unmerged Git candidate attestation

**Purpose:** prove the **local Git commit graph** of the 11 stacked Draft/Open PRs (#132–#141, #143) is complete at an exact pre-Stage4 test candidate **without merging or deploying any PR**. This is not independent human review and does not replace live GitHub status, approved target or runtime QA.

## Exact source

The documented 2026-10-16 GitHub snapshot pins main `d78717ddb9533da6d81840f93abff6b6540137ee` and exact PR head SHAs, branch names, claimed bases and PR numbers. `docs/governance/stage4-premerge-candidate.json` is deliberately immutable-by-default for this checkout. Any branch tip drift, missing fetched branch, changed main head, rewritten/non-descendant branch, incomplete stack, changed SHA, checkout mismatch or forged approval status fails the push candidate report. It does **not** call the GitHub API, discover reviewers or claim that snapshot is live. To refresh the snapshot requires actual reinspection and a reviewed code change.

## Candidate proof

The protected read-only full CI job already fetches repository history `fetch-depth: 0` with `persist-credentials: false`. In the Sprint 131 **push event only**, the attestor reads existing remote-tracking `refs/remotes/origin/*`, the local candidate `HEAD`, exact `GITHUB_SHA`, and candidate tree SHA, verifies all 11 pinned tips and every `merge-base --is-ancestor` relation from pinned main to final Sprint 130 HEAD to candidate. It never fetches, creates a merge commit, pushes or modifies Git. Structured report goes to `TestResults/fullci-stage4-git-candidate.json`. Negative tests exercise missing/rewritten ref, wrong base, missing ancestor, forged merge/review, incomplete stack and non-push context. A valid source report means only `PREMERGE_GIT_ANCESTRY_CONSISTENT` and **still** `stage4_test_admission=DENIED`, `stage_deployment_authorized=false`, `production_release_allowed=false`.

For PR-event CI, only offline negative unit tests run. Git attestation is intentionally push-only, since a GitHub-generated PR merge-checkout `GITHUB_SHA` is not a real development branch tip. GitHub reviewer independence, live PR open/draft status and post-checkout drift must be checked separately immediately before any authorized merge or Stage preparation.

## Admission and remaining blockers

- Candidate CI follows full inherited 14-job regression including real synthetic-only PostgreSQL restoration with ACL/RLS parity and Chromium. This attestor does NOT independently sign or attest other job results and cannot turn a passing CI into a Stage admission decision.
- External Stage4 target, approved non-production identity/data/network, independent reviewer sign-off, explicit merge/Stage test authority, Stage-specific backup/rollback and formal QA remain **blocked** under Issue #142.
- No real patients, insurer, doctors, payment, external AI API, legal or clinical approval, Merge, Stage, production or national first-release authorization. `DENIED_BY_DEFAULT`.
