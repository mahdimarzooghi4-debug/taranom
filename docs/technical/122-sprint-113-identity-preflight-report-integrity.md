# TRN-TECH-122 — Sprint 113 identity TLS report integrity

Status: **technical hardening only; not identity integration authorization**.

## Problem and scope
The public/no-target branch of the existing identity TLS preflight used an ordinary file write that could truncate a pre-existing operator report or follow a symlink. The private-target report path already used exclusive no-follow creation. Both paths must preserve existing report bytes.

## Contract
- Default (no private target) runs remain **offline**, report `TARGET_NOT_CONFIGURED`, and never imply identity verification, membership activation, or national release.
- The generated public/CI report must use exclusive, no-follow file creation with owner-only initial permissions. Existing destination files and symlinks must cause nonzero denial without modification.
- Failure output must not disclose external hosts, credentials, report contents or exceptions.
- This does not select an IdP, implement a BFF, authorize live login, establish data-processing rights, or satisfy `TRN-TECH-012`.
- Changes are confined to the existing identity transport preflight and its regression tests.

## Verification
`python -m unittest tests/policy_checks/test_identity_tls_preflight.py` (or matching unittest discovery); full repository CI on the exact PR HEAD. Add negative tests for existing files and symlinks and positive permissions/denial invariants. No Stage, QA Gate, release or production.

## Limits
`O_NOFOLLOW` prevents final-component symlink traversal but not arbitrary hostile parent-directory races. The public CI working directory is assumed controlled; higher-assurance private evidence retains its separate parent-path checks. These checks do not prove any provider contract or endpoint security.
