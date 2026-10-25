# TRN-TECH-211 — Synthetic Assistant Evaluation Batch

**Status:** Draft implementation contract for Sprint 201; offline and non-production.

## Purpose

Provide a reusable batch evaluator so outputs from each internally hosted model candidate can be checked against the same synthetic Persian interaction fixtures.

## Behavior

- Accept only the exact synthetic case schema and the exact response schema defined in TRN-TECH-210.
- Require a nonempty case batch, one output per case, and unique `test:` case references.
- Preserve input order and reject outputs whose `test:` reference does not match the corresponding case.
- Preserve per-case results and produce a deterministic aggregate summary.
- Evaluate clarification, emergency handoff and human referral, Persian response locale, case lineage, and patient choice only.
- Reject malformed or mismatched batch inputs without partial reports.

## Boundaries

This helper performs no model loading, inference, network request, file access, or training. Run it separately for each candidate's locally generated outputs. A comparison can report interaction-contract behavior only; diagnostic and referral accuracy remain unevaluated until a rights-cleared, qualified Persian reference set and approved thresholds exist. All clinical, training, and Production permissions remain false.

## Tests

`tests/ai/test_medical_assistant_eval.py` covers successful aggregation, empty and length-mismatched batches, duplicate case references, and retained authority limits.
