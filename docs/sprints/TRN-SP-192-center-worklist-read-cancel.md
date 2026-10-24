# TRN-SP-192 — DRAFT / Center coordination worklist read cancellation

Parent Draft/Open [Sprint 191 PR #205](https://github.com/mahdimarzooghi4-debug/taranom/pull/205), exact parent `116e1b4c9a200f1758cfd2cc861a4ec07c94ce92`.
Technical: [TRN-TECH-201](../technical/TRN-TECH-201-center-worklist-read-cancel.md).

Deliver safe human cancellation of Center's actual synthetic PostgreSQL worklist paging, same-page delta refresh and all-row independent journal attestation. Prevent late success/error from resurrecting a prior worklist, stop additional per-row GET after audit cancellation, and revoke stale list when the parent Center command/recovery interlock begins. No Cancel for command POST or uncertain exact UUID recovery, no new endpoint/schema/role/clinical policy. Five real Chromium/Kestrel/PostgreSQL race tests, complete 14-job exact-HEAD CI, technical COMMENTED review only. Keep Draft/Open/Unmerged, no Merge/Stage/QA Gate/Release Approval/Production, national three-market launch DENIED_BY_DEFAULT.
