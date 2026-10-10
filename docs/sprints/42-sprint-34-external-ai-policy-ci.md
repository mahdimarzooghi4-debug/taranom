# TRN-SP-34 — Deny known external AI service runtime dependencies in CI

**Date:** 2026-10-10. Base Sprint33 `sprint-33-internal-ai-human-promotion-boundary`, Draft PR #40. Business TRN-BIZ-037 internal-only AI policy → TRN-TECH-043 → source-code enforcement → adversarial unit tests → exact-SHA CI → preliminary Code Review.

**Scope:** Rather than adding another synthetic AI committee journal, establish a reusable first-party **real CI negative regression gate** for accidental external AI integration.

- **BL-340**: Python stdlib checker scans all executable `src/apps/backend/ai/services/workers` source and manifests/lockfiles for known prohibited external AI SDK packages/imports, external inference API hosts and provider keys/endpoints. No guesses about internal model, hosting or training algorithm.
- **BL-341**: deterministic fail-closed root/UTF-8/symlink/read handling; excludes docs/tests/build artifacts, stable line/rule diagnostic without source/secret values; accepts internal ML libraries and ordinary future legally approved non-AI integrations.
- **BL-342**: 22 isolated adversarial stdlib Python tests for vendor classes, keys, Python/JS/.NET SDK references, package manifests, nested worker paths, legible failure, symlinks, missing runtime tree, non-AI allowcases and deterministic output. No network/credentials.
- **BL-343**: wire policy tests + actual repository scanner into inherited **internal-contract-tests** job of new 12-job workflow on Sprint34 branch and PR against Sprint33; preserve PostgreSQL, clinical DENY-ONLY and Chromium regression gates.
- **BL-344**: update misleading README "no code exists" statement: synthetic .NET and disconnected web UI now exist, while none are operational or Production-ready. Document scope and limitations, preliminary security/source Code Review COMMENT and exact-HEAD CI evidence.

**Limitations:** Regex/static checks are an additional defense, **not** firewall, production egress control, exhaustive supply-chain vetting, obfuscation detection, external security certification, real AI model approval, or permission to process patient data. No external model/AI API, actual data, inference, Stage, QA, Release or Production. Keep all stacked PRs Draft/Open/Unmerged.
