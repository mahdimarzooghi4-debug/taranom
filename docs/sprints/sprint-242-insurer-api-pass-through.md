# Sprint 242 — Insurer calculation API pass-through boundary

## Decision

The insurer owns its calculation. Taranom does not reproduce, infer, or change insurer benefit rules. The future integration will submit provider-defined input and relay the insurer response to the authorized center/provider context.

## Delivered

- A provider API transport interface that treats the insurer request and response as opaque bytes.
- A synthetic-only relay contract that preserves the provider HTTP status and response bytes without converting them into a Taranom eligibility or payment decision.
- A one-megabyte bound for each test message and denial of non-synthetic provider references.
- Unit tests for pass-through, a provider-returned denial status, invalid scopes, size bounds, and malformed responses.

## Not activated

No actual insurer schema, endpoint, credentials, network client, member data, or production route is included. The provider-specific request/response mapping can be implemented only against that insurer's supplied API contract. Taranom-side service fees or other non-insurer amounts remain outside the insurer calculation and require their own approved contract.
