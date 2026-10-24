# TRN-TECH-142 — Sprint 133: actual offline AI QA control-plane HTTP and React

**Implementation:** a genuine, runnable *synthetic-only* Python stdlib localhost HTTP server using Sprint 132's immutable SQLite registry, and a new Persian RTL React route `/operations/ai-lab` rendered from an actual Vite build served on loopback. The admin-looking surface is NOT actually authenticated or an operator authorization mechanism. Launch gated on both `TARANOM_AI_HTTP_TEST_ONLY=CI_EPHEMERAL_ONLY` and `TARANOM_AI_OFFLINE_TEST_ONLY=CI_EPHEMERAL_ONLY`; never expose/reverse proxy beyond localhost.

## End-to-end capability
- `GET /internal/ai-test/v1/inventory?tenant=test:...` reads the persistent frozen datasets, model version metadata and independently held-out numeric evaluation results from SQLite, tenant-filtered and with explicit authority-denial flags.
- `POST /internal/ai-test/v1/datasets`, `/models`, `/evaluations` use immutable checked source manifests, version refs, idempotency and mathematical MAE/MSE from Sprint132. No model weights/training, real data, legal consent, real provider API, medical diagnosis, human promotion or Production activation exists.
- Loopback host/port and same-origin are checked; cookies, Authorization claims, forged tenant/actor/forwarded headers, CORS preflight, unknown routes and PUT/PATCH/DELETE are refused; CSP/security/no-cache headers are mandatory. No credential tokens.
- The React form can freeze a sample **unverified test manifest**, register only synthetic model metadata and evaluate synthetic observed/predicted figures. It refuses forged authorization response flags and marks the data clearly nonclinical and nonauthorized.
- Dedicated stdlib Python real HTTP tests and Playwright Chromium browser tests run inside inherited exact-head 14-job full CI. The separate test host is started *after* the original ops-web tests; it is shut down after this job; nothing is deployed.

**Not achieved:** genuine per-tenant admin identity/consent, legal data ingestion, PostgreSQL-backed Production registry, model loader/adapter, AI training, actual clinical evaluation, human committee authorization or Stage4. Actual AI administration must later be integrated via a real .NET governance API with permitted role/session, verified rights and explicit human promotion; the current demo must never be reused as that endpoint.

The unmerged stacked PR remains Draft/Open, no Stage/QA/Release/Production and nationwide launch DENIED_BY_DEFAULT.
