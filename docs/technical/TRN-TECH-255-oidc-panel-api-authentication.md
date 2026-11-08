# TRN-TECH-255 — OIDC authentication boundary for panel API hosts

## Implemented technical contract

`Taranom.PanelAccess.AspNetCore` now provides `AddTaranomOidcPanelApiSecurity` for an API host that uses a trusted OIDC issuer. The host must configure:

- `Taranom:Authentication:Oidc:Authority` — HTTPS discovery authority;
- `Taranom:Authentication:Oidc:TrustedIssuer` — exact HTTPS token issuer expected by policy;
- `Taranom:Authentication:Oidc:Audience` — exact API audience.

Missing or malformed values fail during host registration. JWT validation requires issuer, audience, signature, and lifetime; inbound claim remapping and token saving are disabled; token validation error details are not returned. The extension also registers the existing named panel capability policies and deny-by-default policy for unbound endpoints.

## What remains denied

Successful OIDC authentication does **not** grant access to a panel operation. `DisconnectedPanelAccessEvidenceSource` remains the default, so a protected endpoint still denies until an API host registers a reviewed server-side evidence provider that resolves current grants, revocation, audit readiness, resource ownership and scope. Request parameters and browser claims do not become authoritative through this change.

This is an authentication foundation only. It does not add an API host, browser BFF/session, UI API calls, member/center data routes, role hierarchy, member-to-subject mapping, an issuer deployment, credentials, or production configuration. No patient, clinical, insurer, fund or payment data is exposed or written.

## Verification

The new tests cover valid configuration, strict token-validation settings, deny-by-default policy registration, and failure on missing/insecure trust settings. The local workspace lacks the .NET SDK; exact-head GitHub CI must verify compilation and tests before this contract is considered code-reviewed.
