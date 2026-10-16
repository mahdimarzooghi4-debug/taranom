# TRN-TECH-142 — Sprint 133: real offline AI backend HTTP + Persian technical workbench

Stacked on Sprint 132 executable Python registry. This is a **non-Production test-only API** for the existing durable SQLite Model Registry, frozen synthetic Dataset Builder and exact-decimal held-out Evaluation. Unlike a disconnected demonstration, responses read the actual same SQLite backend and writes pass all existing constraints.

## Exposed bounded test contracts

- `GET /ai-qa`, `GET /ai-qa/app.js`: RTL Persian technical workbench. Three editable JSON command inputs allow human technicians to freeze dataset metadata, register a model version and evaluate synthetic observations. Read-only inventory shows real stored versions and status, with strict response authority-denial verification. No fabricated success or `innerHTML`.
- `GET /internal/qa/v1/ai/inventory?tenant=test:...` reads a declared `test:` partition (not an authenticated user), returning explicit `operatorAuthenticated=false`, `adminAuthorized=false`, `modelActivated=false`, `trainingAllowed=false`, `productionAllowed=false`.
- `POST /internal/qa/v1/ai/datasets` calls immutable `freeze_dataset`; `POST /models` versioned `register_model`; `POST /evaluations` calls real held-out synthetic `evaluate_synthetic`. Concurrency/idempotency and source separation remain in the existing SQLite kernel. No Production/inference/training/activate endpoints.

Security: explicit `CI_EPHEMERAL_ONLY` flag; binds only `127.0.0.1` and a caller-specified test port; host + forbidden forwarded/authorization/cookie/tenant hints fail closed; every JSON POST requires exact same Origin, exact media type and bounded Content-Length; duplicate JSON keys and unknown fields denied. Strict CSP with externally served script, no generic CORS grant or secrets. No HTTP listener starts without test env. Not connected to a real Admin or member session; do not make this public, proxy it into a network, or promote it to Stage without a new authenticated security contract.

12+ genuine loopback HTTP tests in full inherited 14-job CI cover real persistence, data/model/eval flow, replay conflicts, tenant partition read, malicious headers, CORS/origin, security UI and route denials. Training/runtime/model activation/real data and Stage4 remain DENIED. Production-quality admin panel requires verified identity, .NET authorization, approved database, artifact store and eventual authorized Model Registry Adapter.

No Merge/Ready/Stage/QA/Production; national release DENIED_BY_DEFAULT.
