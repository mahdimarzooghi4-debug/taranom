# TRN-TECH-240 — Prescription copy versions and panel capabilities

Status: synthetic internal contract. No operational identity, database, issuer, payer or settlement adapter is connected.

## Prescription copy contract

`SyntheticPrescriptionCopyDomain` retains the opaque source bytes with the source reference, issuer reference, source revision, UTC issue instant, SHA-256 and monotonically increasing copy revision. Requests are bounded to 4 MiB, require `test:` references and exact content hashes, and support idempotent request replay. The reducer never parses the document or decides that its issuer, contents or legal status are valid. The snapshot is in-memory test contract only; durable production retention and encryption remain unimplemented.

## Panel capability contract

`SyntheticPanelAccessPolicy` checks an explicit capability against one exact `test:` scope. It adds no fixed roles or hierarchy, so later RBAC can map verified identities to owner-approved permissions without widening one center, supporter case or Taranom fund to another. Unknown, live and untrusted requests deny. It authorizes no write, approval or payment.

## Remaining inputs

Operational prescription retention requires an authenticated member boundary, consent and source API, encryption/key custody, retention/deletion rules, and privacy review. Production panel use requires an authenticated identity provider and owner-approved role-to-capability matrix. Settlement still requires official insurer/provider sources, a lawful finance operator, a reconciled ledger, and owner/accounting decisions about allocation, backing and corrections.
