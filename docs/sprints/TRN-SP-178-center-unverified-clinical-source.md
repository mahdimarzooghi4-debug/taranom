# TRN-SP-178 — DRAFT / Center clinical-source claim as separate, non-authorizing action

Parent: Draft/Open Sprint 177 PR #191, HEAD `913ae3856db6e3baaad66bf38b66c77022aaa340`.
Technical contract: [TRN-TECH-187](../technical/TRN-TECH-187-center-unverified-clinical-source-claim.md).
Business: TRN-BIZ-046, with TRN-BIZ-035/036/043 separate authority.

Implement an actual second Center QA workflow action from the **existing** care-coordination journal: a human can explicitly attach an **unverified** `test:` source reference to a member-originated synthetic coordination case, without confusing it with a claimed coordinator contact, original request, clinical referral or authorized health-data access. Require exact action-specific UUID/CAS/payload matching, separate PostgreSQL GET acknowledgment, and fail-closed human-only same UUID recovery. Four real Chromium/PostgreSQL browser scenarios plus full exact-HEAD 14-job CI; preliminary technical Code Review COMMENT only. No new domain/backend policy, schemas, live hospital integration, actual clinical document or real member consent. All PRs Draft/Open/Unmerged; national official launch DENIED_BY_DEFAULT.
