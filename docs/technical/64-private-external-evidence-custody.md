# TRN-TECH-064 — Private external evidence custody, not release admission

**Date:** 2026-10-11. **Scope:** TRN-EP-01…13, three national market segments and national coverage; source decisions TRN-BIZ-028/030/035/036/040/041 and TRN-TECH-002 T-03/T-08, TRN-TECH-045.

## Operational problem
The public repo proves only synthetic/development regression and a deny-only list of 17 real external prerequisites. Genuine identity, medical, financial, insurer, institutional and national-coverage documents require **private** custody and independent authorized review. Never commit those documents, a real manifest naming them, sensitive references or their complete file hashes to this public repository.

## Executable bounded contract
The offline stdlib tool tools/policy_checks/check_private_external_evidence.py accepts an external private JSON manifest and private evidence directory on the operator's own machine. Each record points to exactly one existing missing blocker category of exactly one of the 17 national dimensions, and includes an opaque custodian reference, a private relative artifact path, exact SHA-256 of actual bytes and review_state=UNREVIEWED. Manifest is bound to the current exact national blocker inventory bytes via source_inventory_sha256. The tool checks real file bytes (not self-reported filenames), rejects altered/missing/symlinked/overlarge evidence, path traversal, unrecognized/duplicate records, stale source inventory and manifests or evidence located in the public repository. The integrity result contains only anonymous counts and enum error labels. It **never** uploads data, calls an external service, records confidential paths or values in CI, infers document legality, evaluates signer identity, or turns submitted documentation into verified approval.

Approved public template docs/governance/external-evidence-intake-template.json has ZERO entries. CI validates this empty template and negative tests only; no operational record or document enters CI. The real evidence file and manifest must remain outside the Git repository and CI entirely.

Operator example (run only in a private trusted environment, with paths outside repo):
1. Compute exact local sha256sum of docs/governance/national-launch-blockers.json; copy as source_inventory_sha256 into a private manifest.
2. Use an approved private evidence store directory; populate records with approved dimension/blocker names, private relative file names, actual SHA-256 from local file bytes, opaque custodian reference and UNREVIEWED state.
3. Run: python3 tools/policy_checks/check_private_external_evidence.py --root . --evidence-root /private/evidence --manifest /private/intake.json --report-json /private/only-enum-report.json
4. Even INTACT_FILES_UNREVIEWED means only that supplied bytes match hashes and the records map to source blockers. Every external fact, legal right, signer, policy, privacy review, clinical/financial authority, human release decision and Production admission remains UNVERIFIED. No automated promotion path exists.

## Test/admission
Positive empty template and private illustrative binary fixtures; negative digest tamper, stale source, wrong dimensions, unknown categories, path/symlink escape, duplicate and self-approval, public leak paths, malformed JSON, missing files, no sensitive output. Preserve inherited twelve real CI regression jobs, existing national DENIED_BY_DEFAULT check and public source history gate. This sprint contains no invented partner, real credential, PHI, Stage or Production adapter.

**Never treat CI green, file SHA integrity, or a private manifest as independent evidence validation or permission to release.** Independent clinical/legal/security/custodian verification and human owner approval require a separately authorized future contract. All stacked PRs remain Draft/Open/unmerged.
