# TRN-TECH-213 — Synthetic Model Candidate Comparison

**Status:** Draft implementation contract for Sprint 203; offline and non-clinical.

## Purpose

Run the same synthetic Persian interaction fixtures against the two selected internal prototype candidates, Qwen2.5-7B-Instruct and BioPars, and present their contract outcomes side by side.

## Behavior

- Require exactly the selected candidate identifiers and a complete output batch for each candidate.
- Reuse Sprint 200/201 schema, lineage, and safety-invariant checks.
- Report per-candidate passed/failed fixture counts in deterministic candidate order.
- Keep the scope string fixed to `INTERACTION_CONTRACT_ONLY`; do not rank candidates or infer clinical quality.
- Keep clinical use, training, and Production flags false in every comparison result.

## Limits

This function consumes outputs supplied by a caller; it does not run either model. It evaluates no symptoms-to-diagnosis mapping, specialty referral accuracy, or clinical recommendation. Meaningful diagnostic comparison remains blocked on a rights-cleared Persian case set with qualified clinical references and approved thresholds. The function does not provide such a set or invent those decisions.

## Tests

`tests/ai/test_medical_assistant_eval.py` covers shared-case comparison, per-candidate contract counts, incomplete candidate sets, and the non-clinical scope boundary.
