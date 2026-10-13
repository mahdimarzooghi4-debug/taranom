# Sprint 108 — Synthetic consent-source integrity and safe replay

**Date 2026-10-13 | Technical:** [TRN-TECH-117](../technical/TRN-TECH-117-synthetic-consent-source-integrity.md). **Parent:** Sprint107 Draft/Open PR #118 exact `59cb61611c31be38406bfe3211615d6955063d58`, full Push #38400805133 and Mainline #38400814105 SUCCESS 14/14; Code Review COMMENTED only.

**Scope:** remove latest-boolean-only trust vulnerability in test-only PostgreSQL `SyntheticConsentStore`. An independent original-consent purpose/scope/version must have an entire coherent sequence of evidence, related domain-audit hash metadata, and exact request-replay hashes matching stream header before a read, replay or new mutation. Use MVCC read-only snapshots; a writer verifies under existing row lock and aborts on corrupted source. No schema/authorization changes or real data.

**QA:** four actual isolated PostgreSQL mutation/negative tests including forged GRANT with recycled audit, missing replay, mismatched stream version and altered digest, plus intact original grant/withdraw and replay. Entire 14-gate full CI for Push/Mainline, all original clinical/AI/history security tests unchanged, preliminary COMMENT Code Review only after both exact-SHA green.

**Safety:** Synthetic grants are not lawful independent human consent, clinical record access, AI training, money or Production permission. 17 blockers remain BLOCKED_UNVERIFIED, nationwide first launch DENIED_BY_DEFAULT; PR Draft/Open/Unmerged, no Ready/Merge/Stage/QA Gate/Release Approval/Production without owner direction.
