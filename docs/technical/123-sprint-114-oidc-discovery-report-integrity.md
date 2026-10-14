# TRN-TECH-123 — Sprint 114 offline OIDC discovery output integrity

**Scope:** Code-authorized technical hygiene for the already-approved no-target/offline OIDC discovery preflight, stacked on Sprint 113. **Not authorized:** new authentication, IdP selection, live login, proofing, BFF, membership activation, or external integration.

## Security contract
- The no-target CI path stays offline and returns `TARGET_NOT_CONFIGURED` only, with all authentication, external contract and national-release approval flags false.
- Newly written reports use exclusive/no-follow creation with 0600 owner-only initial permissions. Existing report files and final-component symlinks fail closed without alteration; no exception payload or host information appears in user-visible errors.
- The authorized private-target path remains unchanged and retains private evidence custody, source validation, and read-only metadata checks.
- The trusted CI parent directory is an explicit assumption; `O_NOFOLLOW` alone does not defend against malicious parent-directory changes.

## Acceptance
Three new offline regression tests cover existing bytes, symlink target, and new-file permission/release-denial semantics. Run full exact-head CI and preliminary code review. No Stage, QA Gate, release approval, Production, or PR merge is authorized. `TRN-TECH-012` remains NOT CODE-AUTHORIZED; comprehensive national release remains DENIED_BY_DEFAULT.
