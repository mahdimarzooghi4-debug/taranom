# TRN-SP-174 — DRAFT / verified-before-topic worklist admission

Parent: Draft/Open/Unmerged Sprint 173 PR #187, exact base `fd41cde9b272fd5a97d458597edb01d47fb4a0af`.
Technical: [TRN-TECH-183](../technical/TRN-TECH-183-verified-before-filter-support-worklist.md).

This Sprint fixes an integrity defect in the existing source-filtered operational support worklist, not a cosmetic UI or another mock dashboard. SQL filtering based on unverified first-event JSON was capable of hiding a corrupted case. Rehydrate **all** scanned cases from the canonical source first under a single read-only snapshot, verify SHA-256 and paired audit before topic selection, and then paginate the verified matching cases only. Add owner-only transient corruption/restoration probe and sparse-topic multi-batch real HTTP tests. Retain no new clinical, legal, insurer, finance or tenant authority. Require exact-head full shared 14-job CI, technical Code Review COMMENT, Draft/Open/Unmerged and `DENIED_BY_DEFAULT`.
