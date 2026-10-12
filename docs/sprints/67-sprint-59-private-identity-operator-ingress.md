# TRN-SP-59 — Private operator identity-target ingress

**Date:** 2026-10-11. **Base:** Sprint58 PR #66 Draft/Open, exact-head Push #38142266167 and PR #38142287831 SUCCESS 12/12. Technical preliminary Code Review COMMENT only.

**Real technical risk:** a contracted external identity provider's hostname must not appear in CLI argv, public CI artifacts or public repository files. The existing TLS transport preflight needs a real-world safe operator boundary before genuine infrastructure owners can use it. Avoid yet another synthetic medical model or inventing providers/issuer/OIDC contracts.

**Scope:** TRN-TECH-068, secure owner-private one-line target file with descriptor-based symlink/hardlink/permission/metadata checks; reject legacy --host; private non-overwriting 0600 report outside repository; no network when target absent, malformed or output path unsafe. Reuse original DNS/TLS-only diagnostic and purpose independence; add negative/positive offline unit tests and keep inherited 12 CI jobs, public CI supply-chain/history and national deny-only safeguards.

**Out of scope:** provider vetting, actual endpoint selection, live KYC/login/token or member activation, clinical/financial authority, Stage/QA/Production. A successful later TLS check cannot approve anything. PR stays Draft/Open/unmerged; Code Review initial COMMENT only, independent human approval remains open.
