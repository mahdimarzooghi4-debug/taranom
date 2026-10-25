# TRN-TECH-210 — Persian Medical Assistant Evaluation Contract

**Status:** Draft implementation contract for Sprint 200; non-production.

## Model decision recorded

- Initial internal prototype candidate: `Qwen/Qwen2.5-7B-Instruct`.
- Comparison candidate: `BioPars`.
- No model weights are downloaded, loaded, fine-tuned, or activated by this sprint. The model choice is a prototype candidate decision, not a clinical or Production approval.
- Runtime inference remains internal. External model APIs are prohibited.

## Scope

This sprint adds an offline evaluator for Persian synthetic interaction fixtures. It checks only these interface and safety invariants:

1. Persian case and response locale.
2. Clarification behavior against an explicit synthetic fixture expectation.
3. Emergency handoff to emergency care and a human referral when a fixture sets the approved emergency sentinel.
4. Patient autonomy: the response must preserve the patient's final provider choice.
5. Case/output lineage and exact, bounded schemas.

The fixtures contain no diagnoses, symptom-to-disease mappings, medication advice, or specialty labels. The tests therefore do **not** evaluate diagnostic accuracy or the correctness of specialty referral.

## Data and scoring boundary

- Only `test:`-scoped synthetic fixtures are accepted. Patient data and externally sourced medical datasets are rejected.
- The evaluator reports synthetic interaction-contract outcomes only.
- `diagnostic_accuracy` is always `NOT_EVALUATED_NO_QUALIFIED_GOLD_SET`.
- No score can grant rights, scientific review, clinical use, training, model activation, Stage, or Production authority.
- A future clinical comparison requires a separately governed Persian case set with rights cleared and qualified, independently verified clinical annotations. No diagnoses, specialty mapping, reviewer identity, or thresholds are invented here.

## Implementation

- `ai/taranom_internal/medical_assistant_eval.py` validates synthetic cases and candidate outputs without loading a model or making a network request.
- `tests/ai/test_medical_assistant_eval.py` checks valid and rejected fixtures, emergency handoff, patient choice, Persian locale, lineage, and non-authoritative reporting.

## Acceptance

- Offline unit tests pass.
- Real/member inputs, unbounded schemas, non-Persian locale, and forged emergency semantics fail closed.
- Candidate output cannot select the provider on behalf of the patient.
- Reports explicitly leave diagnostic quality unscored and all clinical/training/Production permissions false.
