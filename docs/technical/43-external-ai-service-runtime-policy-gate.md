# TRN-TECH-043 — External AI runtime dependency and egress regression gate

**Date:** 2026-10-10. **Base:** Sprint33 Draft PR #40 (stacked). **Authoritative product policy:** TRN-BIZ-037 forbids external AI service usage in Product runtime. TRN-BIZ-033/036/038/039 preserve nondiagnostic/human-only clinical decisions, independently lawful AI training data, separate joint review and explicit responsible-human Production promotion.

## Problem and deliberately bounded solution

The previous 12-job CI already enforced no external adapter `HttpClient` inside the pure MemberGovernance kernel and disconnected browser CSP `connect-src 'none'`, but did not test a **cross-repository first-party executable source** guarantee against accidental addition of known **external AI SDKs, runtime API hostnames and AI credential/environment identifiers**. Python AI modules are a future approved architectural option only for internal, lawful training/evaluation; generic Python or machine-learning imports (e.g. `torch`, `transformers`, local `safetensors`) are *not* external AI service APIs.

This sprint adds a **no-dependency, deterministic source-policy tripwire** `tools/policy_checks/check_external_ai_boundary.py` and 22 pure stdlib `unittest` tests. It enforces:
- Traverse first-party executable roots if present: `src/`, `apps/`, `backend/`, `ai/`, `services/`, `workers/`. Include nested C#/.NET source/projects, React/TS/JS, Python and package manifests/lockfiles. Require at least one existing scanned first-party source file; invalid UTF-8/read errors fail closed.
- Disallow runtime sources referring to known external AI inference hosts (OpenAI, Anthropic, Google GenerativeLanguage API, Cohere, Together, Groq, Fireworks, Replicate, Perplexity, Mistral, DeepSeek, Bedrock Runtime and Azure OpenAI endpoint pattern).
- Disallow references to known external AI API credentials/endpoints (`OPENAI_API_KEY` etc.) and concrete SDK imports, npm NuGet dependency names or Python requirements where identifiable. Do **not** block general network libraries needed later for legal non-AI providers.
- Do not traverse `node_modules`, compiler `bin/obj`, Vite/Next `dist/.next`, cache or generated test-report folders; those are not directly source-controlled runtime source. Do not scan documents or test fixtures. Symlinks in executable trees fail closed rather than silently resolving to an external directory. Report only filename, line and rule — not secrets or source-line text. Stable sorted finding output.
- A denied CI test deliberately introduces each provider API host/credential/import/dependency and confirms rejection. Also cover non-AI IdP endpoint, acceptable internal model libraries, lockfiles, future worker roots, missed trees, invalid encoding, symlink escape, generated build files and deterministic output.

## CI

The inherited Sprint33 workflow is copied to `sprint-34-external-ai-runtime-policy-gate.yml`, preserving its **12 independently executed jobs**, all previous real PostgreSQL/Chromium/clinical protected-data DENY-ONLY checks, and base branch Sprint33. Inside **internal-contract-tests** *before* .NET build:

```bash
python3 -m unittest discover -s tests/policy_checks -p 'test_external_ai_boundary.py' -v
python3 tools/policy_checks/check_external_ai_boundary.py --root .
```

This means the branch cannot report fully green on an introduced recognized external AI runtime SDK/hostname/key reference in first-party code. The script and tests use Python standard library; no external service, credential, actual health data, token, runtime integration, installed SDK or network call is necessary. Failures should be fixed as security-policy violations before further development.

## Non-claims and required later security controls

This gate is intentionally **static and lexical**. It cannot prove network isolation, discover unknown future providers, resolve arbitrary dynamic/obfuscated URLs, catch a generic `HttpClient` pointed to an opaque external proxy at runtime, inspect transitive package behavior, verify signed dependency provenance, prevent a privileged maintainer from editing a CI workflow, or attest that future Python AI always runs on approved machines. It is NOT a legal/privacy/clinical independent review, an egress firewall, runtime seccomp/network policy or authorized model deployment. Native CI branch protection and environment permissions remain to be decided separately. The allow/block patterns require periodic human security review as new approved internal AI architecture and threats emerge.

Production remains unavailable: no real members/patients, clinical service/insurance/payments, lawful training Dataset, model/algorithm/runtime, independent committee authorization or real Deployment. This sprint adds **no operational functionality** and does not authorize automatic AI training. Existing synthetic governance evidence never promotes a model.

**Process:** PR Draft/Open/Unmerged; no Ready for Review, Merge, Stage, QA Gate, Release or Production without explicit human instruction.
