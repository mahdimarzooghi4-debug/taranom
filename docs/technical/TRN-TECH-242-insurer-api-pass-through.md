# TRN-TECH-242 — Insurer calculation API pass-through boundary

The insurer API is the authority for insurer-side calculations. `SyntheticInsuranceProviderApiRelay` forwards a bounded provider-defined request to an injected transport and returns the provider HTTP status and response bytes as received. It performs no parsing, eligibility inference, benefit calculation, payment computation, or status-to-business-outcome mapping.

The available implementation is only a synthetic unit-test seam: provider references must use `test:`, messages are capped at 1 MiB, and no HTTP client, route, credentials, or endpoint is configured. The exchange identifier is local correlation metadata and is not assumed to be an insurer idempotency key.

A live adapter needs the insurer's supplied API specification, auth/signature requirements, field and error semantics, privacy/security requirements, and a separately approved operational setup. Taranom service amounts and any non-insurer allocations are not part of this relay.
