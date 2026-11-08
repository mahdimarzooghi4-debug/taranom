# TRN-TECH-266 — OIDC bearer middleware verification

The contract test suite now runs the real JwtBearerHandler registered by AddTaranomOidcPanelApiSecurity in an isolated ASP.NET TestServer. The test supplies an in-memory OIDC configuration containing an ephemeral RSA signing key. The middleware does not contact a live identity provider and no identity provider secrets or real users are used.

The accepted-token case checks that a signed token with the configured issuer, audience, and validity window authenticates and preserves its iss and sub claims with inbound mapping disabled. Rejection cases cover a wrong issuer, wrong audience, untrusted signing key, expired token, unsigned token, and missing bearer token.

This verifies the authentication middleware contract only. It does not verify a real issuer's discovery/JWKS deployment, user onboarding, central grant administration, authorization to a panel route, or production configuration. Central grants remain the only source of dynamic capabilities and scopes; this test does not grant any.
