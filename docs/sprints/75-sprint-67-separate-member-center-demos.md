# TRN-SP-67 — Separate the member and center technical browser environments

**Date:** 2026-10-11. **Base:** Sprint66 PR #74 Draft/Open/unmerged, SHA \`079d371165535a594ac19b123e14c33cb9115b47\`, Push and PR CI 14/14 SUCCESS, Code Review COMMENT.

**Functional work package:** fix an actual architecture UX gap: support workflow incorrectly appeared inside the center demo. Separate member-support React frontend and entrypoint; remove member panel from center build, retain center clinical governance demo and history; keep same actual localhost ASP.NET + PostgreSQL API with no invented auth; build both independent reproducible bundles with their own lockfiles; run real browser smoke for correct deep route, original member support submit/followup, and original center workflows, plus cross-surface/no-external-request safety; preserve full inherited 14-job CI, fix to green and record COMMENT-only technical review.

**Not delivered:** real IDP/proofing, role separation, trusted session, clinical/insurer/finance APIs, activation, Production. Only \`test:\` refs and isolated test database are permitted, and all public responses continue deny-only. All 17 national blockers persist and comprehensive first nationwide three-market launch remains DENIED_BY_DEFAULT. No merge/Stage/QA/Release/Production without explicit owner instruction.
