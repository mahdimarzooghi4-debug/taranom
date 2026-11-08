# Sprint 266 — OIDC bearer middleware verification

## Goal

Exercise the configured JWT bearer middleware with locally signed synthetic tokens, beyond checking option values or replacing authentication in API route tests.

## Delivered

- Run the actual AddTaranomOidcPanelApiSecurity bearer handler in an isolated TestServer.
- Use a static, in-memory OIDC configuration with an ephemeral RSA signing key; no network issuer, real account, or credential is involved.
- Verify a correctly signed token reaches the authenticated identity boundary with its issuer and subject intact.
- Verify wrong issuer, wrong audience, wrong signature key, expired, unsigned, and missing-token requests are rejected.
- Keep panel authorization separate: this test proves authentication mechanics only, not grant issuance, production identity onboarding, or access to protected panel operations.

## Verification and gates

Run the full GitHub regression on the exact PR head. No live OIDC provider, production configuration, Stage, formal QA, release, or Production access is added.
