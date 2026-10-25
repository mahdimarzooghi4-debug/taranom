# Sprint 203 — Synthetic Model Candidate Comparison

## Goal

Compare the two selected internal model candidates on identical synthetic interaction-contract cases without activating either model or claiming medical performance.

## Delivered

- Added `compare_candidate_batches` for Qwen2.5-7B-Instruct and BioPars output batches.
- Reused existing case/output validators and batch summaries.
- Reported separate pass/fail counts in stable candidate order, with no winner ranking.
- Added tests and TRN-TECH-213.

## Limits

No model is loaded or called. No diagnostic or specialty-referral accuracy is evaluated. All clinical, training, and Production flags remain false. A qualified, rights-cleared clinical gold set and thresholds are still required for those evaluations.

## Acceptance

- Both candidates use the same nonempty fixture batch.
- Invalid schemas or incomplete candidate batches fail closed.
- Results make no clinical-quality or model-ranking claim.
