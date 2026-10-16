# TRN-TECH-144 — Sprint 135: Shadow QA workspace and real HTTP end-to-end

Links the working Sprint 134 synthetic-only OLS learner to the Persian React AI lab.
On /operations/ai-lab a bounded synthetic manifest now includes two TRAINING sources and a disjoint INDEPENDENT_EVALUATION source.
Two additional actions call actual same-origin localhost Python HTTP endpoints: POST /internal/ai-test/v1/shadow-training and POST /internal/ai-test/v1/shadow-benchmarks.
Both actions use immutable SQLite-backed records and return Shadow model metadata and held-out Baseline vs Shadow MSE. No winner is chosen.

UI validates every response has syntheticOnly=true, adminAuthenticated=false, operatorAuthorized=false, trainingAllowed=false, inferenceSupported=false, stageAllowed=false and productionAllowed=false.
The persisted inventory now explicitly requires candidate_approved=false, winner_selected=false, well-formed artifact SHA and synthetic-only statuses; any forged approval fails closed.
Exact derived synthetic refs are validated before submission. No real clinical patient record, lawful consent, provider identity, model adapter, inference runtime, governance approval or model promotion is provided.

Acceptance: genuine Python HTTP/SQLite tests validate full fit + independent benchmark persistence/replay and deny training partition leakage, forged model promotion, or impersonated authorization.
Chromium test AI135-01 runs Persian controls through actual HTTP to the SQLite-backed registry and checks persisted results after reload. AI135-02 attempts to forge approved candidate/selected-winner fields and is rejected.
All legacy React browser tests are run unfiltered with both local servers; mandatory inherited 14-job full CI remains required. No Stage or Production access.

Scope: only test: values and CI_EPHEMERAL_ONLY localhost, no independent human clinical approval, no real training rights. PR remains Draft/Open/unmerged; national release DENIED_BY_DEFAULT.
