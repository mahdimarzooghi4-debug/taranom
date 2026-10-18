# TRN-SP-161 — DRAFT / bind synthetic member ACKs to exact journal events
- Parent: Sprint 160 Draft/Open PR #174, exact base `bc064537e96f2a39647eebed131b2cf25ccc5530`.
- Technical scope: [TRN-TECH-170](../technical/TRN-TECH-170-synthetic-command-ack-lineage.md).
- Three local, PostgreSQL-backed flows: proofing correction, late-evidence claimed review, and nonclinical care coordination. Improve client-side write acknowledgement and human-visible uncertain-command recovery rather than adding another reporting screen.
- No production data, no new permissions or schema, no human/clinical/financial decisions. Even recorded events remain unverified synthetic claims. All prior Draft PRs remain unmerged.
- Gate: 14-job shared full CI, exact-HEAD technical Code Review COMMENT only. Stage/QA/Release/Production DENIED_BY_DEFAULT.
