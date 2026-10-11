# TRN-TECH-068 — Private operator identity-target ingress and confidential report boundary

**Date:** 2026-10-11. **Parent:** TRN-TECH-067 / Sprint58 Draft PR #66. **Business:** TRN-BIZ-018…024; TRN-TECH-002 T-01/T-03/T-08; TRN-TECH-012 member-session candidate; national DENIED_ONLY.

## Risk

The Sprint58 transport-only TLS diagnostic correctly avoids selecting an identity operator, contacting providers in CI, sending credentials, or asserting an authentication or KYC result. However \`--host name\` can expose a genuinely selected private vendor hostname in process arguments / shell history, and its original report output path did not enforce private custody. That would unnecessarily expose partner information in a **Public** source-control project. The transport diagnostic is intentionally operator-only and does not constitute proof of actual provider selection or authority.

## Implemented private operator boundary

- **Remove usable command-line hostname option.** The legacy \`--host\` argument is rejected before DNS or report creation without printing its value. Actual operator invocation must use \`--target-file\`, an external trusted *one-line ASCII public DNS hostname* file, and \`--root\` specifying public repository root (default inferred from script). Omitted target remains \`TARGET_NOT_CONFIGURED\` and performs no network.
- Read real target only from an owner-owned POSIX regular file, size bounded, permissions no broader than owner 0600, single hardlink, no symlinks in path, outside public repository both lexically and canonically. Open with O_NOFOLLOW and verify descriptor inode/size/mtime/ctime against post-read values. Reject altered, missing, aliased, multiline, invalid, URL/userinfo/IP/private DNS host without network. Existing DNS pin/SSRF guards and standard TLS CA+SNI/peer validation remain.
- For nonempty private targets, report destination **must remain outside Public Git**, with no symlink parent, no existing output, and no overwrite. Write only redacted standard report with atomic O_EXCL creation and private 0600 mode. Refuse an unsafe destination **before DNS or TLS**. No provider hostname, IP, certificate data or credentials enter report or tests.
- All GitHub Actions still use only the *unconfigured no-network mode*. Inherited 12 CI jobs, exact-source Git history safety, pinned workflow trust, national DENIED gates and independent PostgreSQL/Chromium regressions remain.

## Private authorized operator use

1. Independently select and contract the *actual* licensed identity-proofing provider and, separately, an authentication IdP. These decisions are **NOT made here**. Verify lawful rights and approved operating context outside repository.
2. On a trusted POSIX operator machine, create **outside repository** a single-hostname file under a private directory and set restrictive permissions \`chmod 600 /private/identity-host.txt\`. Never commit the real hostname file, manifest, report or actual credential to public Git/CI.
3. Run only after independent operator authorization:
   \`python3 tools/policy_checks/check_identity_tls_preflight.py --purpose AUTHENTICATION_IDP --target-file /private/identity-host.txt --report-json /private/identity-transport-report.json\`
4. An actual \`TLS_TRANSPORT_REACHABLE_ONLY\` is just limited TLS transport. It does not authenticate a member, select or validate provider, authorize KYC, approve contracts, warrant a legal data basis, approve Stage, member activation or Production. Identity proofing requires a separate operator-selected purpose and independent approval.

## Tests/limits

Offline simulated socket tests cover valid owner-private target, read-only ingress, no argv credential leakage, public in/out rejection, file permission, hardlink/symlink/parent symlink, no-clobber report, multiline/untrusted hostname, missing target, and existing DNS/TLS protections. POSIX local filesystem checks reduce accidental exposure; they are **not** secure custody against malicious root/host administrator, mount manipulation, external metadata leakage, compromised shell/process memory, fake vendor statements or unreviewed operator actions. No new clinical, financial, OIDC/session or live service API is introduced.

**Always:** external_provider_contract_verified=false, member_authentication_verified=false, identity_proofing_verified=false, member_activation_allowed=false, national_release_allowed=false. All 17 dimensions remain BLOCKED_UNVERIFIED; first official comprehensive three-market national release DENIED_BY_DEFAULT. PRs Draft/Open/unmerged; no Stage/QA/Production without owner approval.
