# TRN-SP-21 — Dual synthetic provenance checks on historical receipts

**Date:** 2026-10-10. **Proposed base:** Sprint 20 `sprint-20-historical-receipt-replay-fence`; intended PR Draft/Open/Unmerged.

**Governance chain:** Business TRN-BIZ-043/044/053/054 → TRN-TECH-030 → Scrum backlog → Sprint 21 → Code → Tests → preliminary Code Review.

- BL-210: exact-command replay second source check under request/packet PostgreSQL locks; fail closed on source unavailability, changed issuer identity/version, late revocation and request rebinding.
- BL-211: independent read-only historical inspector second check after the original PostgreSQL historical read, introducing explicit synthetic source-drift status; no operational permission.
- BL-212: preserve conflict replay precedence, paired audit/integrity validation, historical issuer mismatch distinction, unchanged rows, and exactly one durable receipt/audit.
- BL-213: implement all new real PostgreSQL negative and stable-path test cases; run full inherited 12-job CI and retain evidence at exact HEAD.
- BL-214: conduct source-level preliminary code/security review; independent medical/security/legal approval remains open.

**Excluded:** external identity/entitlement/revocation adapter, fake policy, signed official issuer, privacy consent changes, actual acknowledgement, verified clinical credentials, doctor activation, production APIs/UI, real patient/insurer/payment access, or deployments. All synthetic `test:` data and DENY-ONLY protection boundaries remain unchanged.
