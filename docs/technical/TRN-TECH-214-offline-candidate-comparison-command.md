# TRN-TECH-214 — Offline Candidate Comparison Command

**Status:** Draft implementation contract for Sprint 204; offline and non-production.

## Command

`python ai/tools/compare_medical_assistant_candidates.py` reads one JSON document from standard input and writes one JSON report to standard output. Input is capped at 8 MiB. Duplicate JSON keys, non-standard numeric constants, malformed Unicode/JSON, unexpected top-level fields, and invalid synthetic case/output records are rejected with a generic error and nonzero exit code.

## Input contract

The document has exactly two fields: `cases` and `candidate_outputs`. Cases use the Sprint 200 synthetic Persian schema. Candidate outputs contain exactly the selected internal prototype identifiers and one output per case, as defined by Sprint 203. The command does not accept filesystem paths or fetch data.

## Limits

The command does not load or call a model, access the network, read files, write datasets, or train. It only evaluates supplied synthetic interaction-contract records. Its report cannot grant clinical, training, Stage, Release, or Production authority and does not score diagnosis or specialty-referral accuracy.

## Tests

`tests/ai/test_candidate_comparison_cli.py` invokes the command as a subprocess and checks valid reporting, duplicate-key rejection, and rejection of non-synthetic fixtures.
