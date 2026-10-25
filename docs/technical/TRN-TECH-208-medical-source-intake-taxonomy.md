# TRN-TECH-208 — Local source-package integrity and configurable taxonomy

## Goal
Extend Sprint 197 without inventing clinical categories, importing copyrighted content, or promoting a source to training data. The owner/qualified reviewers supply taxonomy labels; the library validates structural integrity only.

## Implemented
- \`freeze_taxonomy\`: deterministic, versioned SHA-256 taxonomy manifest; exact schema, unique category refs, one scheme per manifest, known parent references and cycle rejection. Every category remains scientifically unreviewed.
- \`verify_source_package\`: accepts article bytes and license-evidence bytes supplied by the caller (no URL fetch, network, filesystem persistence, extraction or indexing). Enforces byte bounds, checks declared SHA-256 values against actual bytes, validates a bounded set of taxonomy assignments, and emits a deterministic package fingerprint.
- The output proves **byte integrity only**. It deliberately keeps \`rights_verified\`, \`license_semantics_reviewed\`, \`scientifically_reviewed\`, \`taxonomy_reviewed\`, \`training_allowed\`, \`clinical_use_allowed\` and \`production_allowed\` false. A free-to-read URL or matching digest is not permission to reuse or train.
- Tests use synthetic bytes and test-prefixed taxonomy labels. No real article, patient data, doctor identity, clinical mapping, external model, or deployment is involved.

## Remaining blockers
1. Select source catalogs and taxonomy labels through management and qualified clinical/legal review.
2. Verify each publisher's terms and reuse/training rights against independent evidence; a local hash does not establish legal interpretation.
3. Obtain approved scientific review and retraction/version handling before any article content is used.
4. Keep actual data import, training and model activation denied until explicit consent, legal authorization, independent evaluation and human activation gates exist.

This is an offline intake-validation component, not an article crawler, clinical knowledge base, trained model or Production capability. The national launch remains \`DENIED_BY_DEFAULT\`.
