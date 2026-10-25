# Sprint 198 — Medical source intake integrity and configurable taxonomy

**Status:** Draft implementation candidate; stacked after Sprint 197/CI repair; no merge, Stage, formal QA Gate, Release or Production.

## Scope
- Verify declared article and rights-evidence SHA-256 against caller-supplied bytes in memory, with strict byte bounds.
- Freeze a configurable category taxonomy with a deterministic digest, parent validation and cycle rejection; do not choose clinical category labels.
- Bind source metadata to taxonomy references and a deterministic intake fingerprint.
- Keep legal rights, scientific review, training, clinical use and Production denied regardless of successful hash checks.
- No URL fetch, network access, persistence, article extraction, patient data, real doctor matching or model training.

## Acceptance
- Synthetic-only tests cover valid package integrity, digest tampering, malformed schemas, oversized inputs, unknown/duplicate categories, parent cycles and forged authority flags.
- Existing 14-job full regression remains mandatory; PR stays Draft/Open/Unmerged.
