# TRN-TECH-065 — Private evidence ingress and path-race safety

**Date:** 2026-10-11. **Parent:** TRN-TECH-064 / Sprint55 PR #63. **Business/Technical:** TRN-BIZ-028/030/035/036/040/041, TRN-TECH-002 T-03/T-08, TRN-TECH-045 release blockers.

## Identified implementation risk
A private evidence artifact validated with Path.is_symlink()/is_file()/resolve() and then opened by its pathname could be swapped between validation and read. A hash of an already opened file is insufficient to establish that the same source pathname still names the file. An exported local report also must not accidentally be written into Taranom's public Git tree. This is concrete security work on the existing real-private-file intake boundary, not another synthetic business read model.

## Bounded technical contract
- Keep the 17-dimension source blocker inventory, all UNVERIFIED and national DENIED_BY_DEFAULT. Only an operator with separately authorized PRIVATE local documents can run nonempty intake. CI exercises only the zero-evidence public template and synthetic fixtures.
- For bounded manifest JSON, use a non-following file descriptor, bounded read and before/after file identity checks; reject changes/links before interpreting contents.
- For actual private documents, root the traversal at an opened directory file descriptor; traverse relative child descriptors with O_NOFOLLOW/O_DIRECTORY. Reject symlinked path components and hardlinks, nonregular files, changed size/timestamp/inode/metadata, changed final path or swapped directory entries; hash from the same descriptor and compare exact expected SHA-256. Constrain to Linux/POSIX with safe descriptor operations; no cross-platform fallback that follows symlinks.
- Reject private manifest paths and evidence roots within the public repository, including lexical symlink aliases. A **nonpublic** manifest cannot write any report into the public tree; output remains redacted status and counts, with no source path, file bytes, hashes or custodian identity.
- Test nested private file success, intermediate/root symlink, hardlink, deterministic filename swap *during* actual descriptor read, public alias and public report denial, private-only redacted report. Preserve all previous negative/positive cases and the inherited 12-job CI chain.

## Limits and authorization boundary
This protects against common path-substitution attempts in a trusted local operator environment; it is **not** a secure enclave or a complete defense against malicious privileged processes, alternate mounts, inode re-use, filesystems lacking POSIX semantics, real evidence theft, signing/issuance fraud or concurrent tampering under arbitrary filesystem control. Directory/file metadata comparisons are best-effort and cannot certify the external provenance of a file. SHA-256 is byte-integrity evidence only. No legal, financial, medical, national network, research or AI approval is inferred.

All files containing actual sensitive evidence, real manifest references and operator review decisions must remain PRIVATE. Any result still has release_allowed=false, external_evidence_verified=false and independent_review_observed=false. The official comprehensive first launch across all three markets remains DENIED_BY_DEFAULT. All PRs Draft/Open/unmerged. No Stage, QA Gate or Production.
