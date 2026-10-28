# TRN-TECH-245 — Stable panel capability tokens

## Contract

The v1 grant format remains `v1|<scope-length>|<scope>|<capability-token>`. Sprint 245 replaces enum reflection with explicit token mapping. Existing token spellings are preserved:

| Capability | v1 token |
| --- | --- |
| Member own prescription read | `MemberOwnPrescriptionRead` |
| Center roster manage | `CenterRosterManage` |
| Center appointments manage | `CenterAppointmentsManage` |
| Center revenue read | `CenterRevenueRead` |
| Taranom fund read | `TaranomFundRead` |
| Supported case finance read | `SupportedCaseFinanceRead` |

An enum rename no longer changes the wire value. A newly added enum member remains unusable until a deliberate token mapping and tests are added. Unknown, differently cased, or malformed tokens deny.

## Authority boundary

The mapping does not establish who may receive a capability and does not validate identity or resource provenance. The policy still requires a trusted authentication handler, trusted issuer configuration, and server-side resource resolver that are not configured by this sprint.
