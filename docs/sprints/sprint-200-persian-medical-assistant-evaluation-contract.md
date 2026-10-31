# Sprint 200 — Persian Medical Assistant Evaluation Contract

## Goal

Prepare a real, executable offline test contract for the selected internal model prototype: Qwen2.5-7B-Instruct.

## Delivered

- Added an evaluator for synthetic Persian conversation fixtures.
- Added executable checks for Persian output, clarification expectations, emergency-to-ER plus human handoff, patient final choice, strict schemas, case lineage, and rejection of forged aggregate results.
- Added explicit reporting that diagnostic accuracy is not evaluated without a qualified gold set.
- Documented that this sprint does not download or run either model and does not create clinical, training, Stage, or Production approval.

## Limits and follow-up

The fixtures contain no symptom-to-disease or symptom-to-specialty labels. A qualified, rights-cleared Persian clinical evaluation set and its acceptance thresholds remain future inputs; the code does not invent them. A green software test confirms only the evaluator contract, not medical performance.

## Acceptance

- New offline tests pass with the repository's AI tests.
- Invalid, non-synthetic, non-Persian, or authority-expanding data is rejected.
- Reports preserve patient choice and keep clinical use, training, and Production disabled.
