# TRN-TECH-079 — Private batch evidence preparation and gap workbench

**Date:** 2026-10-12. **Scope:** TRN-EP-01…13, MARKET-PUBLIC, MARKET-SUPPORT, MARKET-CORPORATE and NATIONAL-COVERAGE. **Parent:** TRN-SP-69 / TRN-TECH-078. **Decision sources:** TRN-BIZ-028/030/035/036/040/041, TRN-TECH-045/064/065/068. **Code authorization:** owner instruction of 2026-10-12 for a meaningful non-deploying package, restricted to private operator tooling.

## Real operational gap
The existing `check_private_external_evidence.py` verifies a **pre-prepared** private manifest against the canonical 17 release-blocker inventory and actual files. An operator still has to manually calculate every file digest, copy exactly matched blocker labels, bind the inventory digest, and find which national-market obligations have not even received any **unreviewed** file. This is error prone and discourages collecting actual lawful external partner evidence. Adding simulated clinician, wallet or identity workflows instead would not reduce this practical prerequisite gap.

## Executable contract (not approval)
1. A trusted POSIX operator prepares one private owner-only JSON batch-request file **outside Public Git**, containing exact `schema_version=1`, `scope=PRIVATE_EXTERNAL_EVIDENCE_BATCH_REQUEST`, and a bounded `entries` array. Each entry contains only `dimension`, `blocker`, `artifact` (relative to the private evidence root) and an opaque `custodian_ref`. No real provider, record, key, file path or credential appears in the repository or CLI arguments.
2. `build_private_evidence_batch.py` checks the current exact repository-owned 17-dimension `DENIED_BY_DEFAULT` source (including all three markets and nationwide scope), the owner-only private request, every mapped blocker reference and each actual bounded file's bytes through the **existing secure descriptor-based custody hashing implementation**. Duplicates, unknown blocker names, symlinks, hardlinks, traversal, changed files, unsafe sources and claims of self-approval fail closed. Digests are computed by the tool, not supplied as authority by the operator request.
3. It creates a private **new-only, mode 0600** manifest compatible with the already existing independent `check_private_external_evidence.inspect` verifier, binds it to the exact inventory SHA-256, and re-verifies the full manifest. An additional private redacted gap report covers all 17 dimensions, giving only public blocker **codes** in `unreviewed_file_slots` and `still_without_file_slots`, anonymous counts, and permanent false authority fields. It must not expose filenames, hashes, custodian refs, target domains, file bytes or actual identity data. Destination parents may not be symlinked or public. Never overwrite a prior file or emit private values in stdout/stderr.
4. Denied/invalid, unavailable or mutated files produce **no successful output** and no activation. Created incomplete outputs are cleaned up on error. No network access, provider selection, login, credentials, tokens, payments, legal verification, clinic/insurance approval, AI model, medical data or Stage/Production actions.
5. The only success label is `UNREVIEWED_FILES_INTACT_NOT_APPROVED`. Even 100% *submitted file slots* does not establish authentic sources, lawful permission, contract validity, quality review, independent human acceptance, Product completion or Release readiness. The 17 canonical blocker states remain `BLOCKED_UNVERIFIED` and official three-market nationwide launch remains `DENIED_BY_DEFAULT`.
6. Tests first: real temporary private bytes across three markets and technical Epics, hostile/symlink/hardlink/traversal/public-path/stale-inventory/duplicate/schema/review-escalation negative cases, no-clobber and privacy/redaction. Add tests to an inherited 14-job full Push/PR CI matrix without dropping any job, browser, DB, external-AI, secret-history or release-denial check.

## Operator contract
Use an independently approved trusted local workstation only. Put the request, evidence and both destinations outside the Public repository. `chmod 600` the request. Example **paths only**, no implied real vendor:

```sh
python3 tools/policy_checks/build_private_evidence_batch.py \
  --root . \
  --evidence-root /private/approved-evidence \
  --request-file /private/batch-request.json \
  --output-manifest /private/new-unreviewed-manifest.json \
  --output-report /private/new-redacted-gap-report.json
```

Use `check_private_external_evidence.py` again on the generated private manifest before any subsequent custodian review. All actual review/verification/authority and all 17 launch blockers remain separate gates. Do not run real files in CI. No independent human approval is implied by this technical package. Keep PR Draft/Open/Unmerged and do not deploy.
