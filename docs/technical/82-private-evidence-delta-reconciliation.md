# TRN-TECH-082 — Confidential evidence change reconciliation across intake snapshots

**Date:** 2026-10-12. **Decisions:** TRN-BIZ-028/030/035/036/040/041, TRN-TECH-045/064/065/068/079/080/081. **Parent:** Sprint72 Draft PR #80 exact SHA `b9a724bd3e7b627c256e5e26ebc66c93a096aac0`, same-head Push and PR CI 14/14 SUCCESS, preliminary COMMENT-only Code Review (not independent human acceptance).

## Material operational problem
The operator can privately produce a real byte-verified manifest and 17-dimension gap report (Sprint70). But evidence from licensed providers, insurer/financial authorities, clinical teams and national coverage changes over repeated collection rounds. Without a byte-attested *previous vs current* reconciliation, an operator may miss a withdrawn document, quietly changed terms or a replaced file. Manual document comparison is risky and sharing raw document identifiers or hashes with Public Git/CI would disclose real partner details.

## Scope and authorized contract
1. Offline, private operator-only Python command `compare_private_evidence_snapshots.py`, with **two separately configured evidence directories and two owner-only private manifests**. No live provider chosen or contacted, no actual patient/identity information in repository or CI. The inputs are exactly the unreviewed manifest scheme already approved by TRN-TECH-064/079; this tool cannot turn an unreviewed receipt into a human/clinical/legal approval.
2. Each immutable input *snapshot* must pass existing descriptor-pinned real-byte validation `check_private_external_evidence.inspect` against the current canonical 17-dimension source. The manifest is separately read with the existing owner-only 0600/no-follow/one-link bounded reader before and after validation so a replaced manifest is denied. Old and new must each contain at least one actual valid file. Different private evidence roots are required: later substitution must not erase the earlier files. Source policy must be exactly the same 17 dimensions with every `BLOCKED_UNVERIFIED`; stale policy hashes, missing source, invalid/reforged files, symlinks, traversal, hardlinks, missing or changed custody fail closed. No cross-repository data leakage.
3. Reconciliation compares multiset **SHA-256 content identity inside the same exact (dimension, blocker) source slot**, not file names or custodian claims. The private report records only official public blocker category strings, per-slot byte-identical retained counts, newly provided or changed counts and removed/replaced counts, and their unreviewed presence/absence. Duplicate byte-identical documents do not impersonate independent review; counts are file records only. Any slot with both removed and added bytes is `REPLACED_OR_CHANGED_UNREVIEWED` (not validated contract transition). No private file names, paths, hashes, opaque refs, document bytes or real organizations in report, CLI output, CI or logs. Covers all 13 Epics, three markets and national coverage; never shrink the denominator.
4. Output is **new-only 0600**, in an owner-only private directory outside public repository and separate from both manifest and evidence roots. Reject existing output or symlinks; on failed write/fsync clean only newly created inode, never clobber older report. Stop/report only sanitized error codes. No network, token, encryption-key invention, reviewer identification, database, vendor, rate, geographic target or timing threshold.
5. Status is `UNREVIEWED_EVIDENCE_DELTA_NOT_APPROVAL`. Every dimension remains `BLOCKED_UNVERIFIED`; `external_evidence_verified=false`, `independent_review_observed=false`, `release_allowed=false`, `national_release_allowed=false`; official first complete nationwide three-market release `DENIED_BY_DEFAULT`. Output does not measure overall product completeness or count real coverage.
6. Add real temporary private-binary, multi-round tests: retained, new, removed, replaced, same bytes under new name, duplicate count, all 17 dimensions, two independent roots, mutated file, tampered digest, symlink/hardlink/traversal and 0600/no-clobber/failed fsync, stale canonical source, private redaction. Add tests to canonical full 14-job inherited shared CI and exact reviewed local Sprint73 dispatcher; both Push and PR must complete 14/14 SUCCESS on exact HEAD. Do not weaken 70+ historic security workflow checks or drop any PostgreSQL, Chromium, AI-exclusion, public history, release-denial tests.

## Private operator invocation
Only **after** independent lawful collection and trusted local custodian assignment. All concrete paths and any identities remain outside Public Git and shell-history values should be treated as sensitive; no real data is sent to GitHub:

```sh
python3 tools/policy_checks/compare_private_evidence_snapshots.py \
  --root . \
  --before-evidence-root /private/batch-one/evidence \
  --before-manifest /private/batch-one/intake.json \
  --after-evidence-root /private/batch-two/evidence \
  --after-manifest /private/batch-two/intake.json \
  --output-report /private/reconciliation/change-report.json
```

The two earlier verified snapshot files and a reviewer-authorized contract/legal analysis must be preserved privately by the operator; nothing here authenticates a counterparty or legal permission. The report is a task aid, not a signed chronology or a release proof. State of TRN-TECH-012 remains NOT CODE-AUTHORIZED. PR Draft/Open/Unmerged, no Ready/Merge/Stage/QA Gate/Release/Production without explicit owner direction.
