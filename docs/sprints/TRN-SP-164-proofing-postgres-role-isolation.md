# TRN-SP-164 — DRAFT / isolate synthetic proofing PostgreSQL reader/writer
- Parent: Sprint 163 Draft/Open PR #177, exact base `297c57e4bfb6b9460b1dd9bda874bdd762109063`.
- [TRN-TECH-173](../technical/TRN-TECH-173-proofing-postgres-role-isolation.md).
- Value: remove schema-owner DB powers from the synthetic proofing correction API and replace locking GET with a genuinely SELECT-only, repeatable-read isolated runtime. Preserve denial audits on a separate writer with INSERT-only denial history; verify overprivileged credentials and cross-domain SQL fail closed at application startup.
- Acceptance: unchanged all 14 mandatory regressions, real local PostgreSQL/Kestrel/Chromium positive and negative proofing scenarios, and exact-head technical Code Review COMMENT.
- Exclusions: other journals, new identity authority, personal data, real KYC/IdP, financial and clinical permission, independent release approval, merge, Ready for Review, Stage, QA Gate, Production. PR Draft/Open/Unmerged and national official launch `DENIED_BY_DEFAULT`.
