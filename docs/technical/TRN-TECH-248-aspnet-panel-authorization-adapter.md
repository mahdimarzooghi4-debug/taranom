# TRN-TECH-248 — ASP.NET panel authorization adapter

## Pipeline

`AddTaranomPanelAuthorization` registers six named policies. Each requires an authenticated principal and a `PanelAccessRequirement`. The handler requests current `PanelAccessEvidence` from `IPanelAccessEvidenceSource`, then calls the existing `PanelAccessPolicy` with configured issuer, freshness snapshot, server-resolved scope, resource-owner scope, and requested capability.

Authorization resource is forwarded unchanged to the evidence source so a future adapter can resolve the target from server-owned endpoint context. The optional default provider is intentionally disconnected and always returns no evidence. Handler exceptions are swallowed at this boundary and remain denials.

## Test coverage

Synthetic xUnit tests exercise the ASP.NET `IAuthorizationService` for exact allowed evidence, disconnected/default denial, missing issuer, anonymous users, capability mismatch, stale permissions, provider exceptions, and resource forwarding.

## Non-operational status

This project is not registered by the center demo host and no production routes use it. No authentication scheme or trusted issuer is configured. The evidence interface has no real implementation. No center, member, support, financial, clinical, or AI data path is enabled.
