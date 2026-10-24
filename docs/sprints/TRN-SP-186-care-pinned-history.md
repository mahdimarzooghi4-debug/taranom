# TRN-SP-186 — DRAFT / Real version-pinned synthetic coordination event history

Parent: Draft/Open [Sprint 185 PR #199](https://github.com/mahdimarzooghi4-debug/taranom/pull/199) at `aee579b5bad0c603d5956cb1a3c65ef7a52d0360`.
Technical: [TRN-TECH-195](../technical/TRN-TECH-195-care-pinned-history-pagination.md).

Backend first: reuse original authenticated-to-test-loopback, audit-replayed least-privileged READ ONLY PostgreSQL care journal and expose strict version-pinned history pagination; reject inconsistent cursor and concurrent revision rather than combining mixed event pages. Client next: independent manual source-backed Member history pages with exact event/authority/UUID consistency checks, without any new commands or access. Test: four real Chromium/Kestrel/PostgreSQL and extended Python HTTP strict-input+stale/lineage cases. Require all 14 canonical CI gates SUCCESS and technical Code Review COMMENTED only. No Merge, human approval, Stage, formal QA Gate, Release or Production. Three-market nationwide release DENIED_BY_DEFAULT.
