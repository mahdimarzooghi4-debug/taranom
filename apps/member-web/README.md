# Taranom Member Web — Sprint 02 authorized code slice

**Scope:** Persian RTL accessible React/TypeScript **member app shell only**. No real user login, account, credit values, clinical records, insurer/provider integrations, payment, AI or synthetic fake real-world authority.

- `npm ci --no-audit --no-fund --ignore-scripts`
- `npm run typecheck`
- `npm run build`
- `npx playwright install chromium` then `npm run test:e2e`

The app intentionally renders every member section in an explicitly unavailable state. Navigation paths are browser routes **only** and do not imply existing .NET read endpoints. It never sends private network API requests or persists sensitive state in browser storage. Pages and tests cover WEB-01, WEB-04…12 under this **shell-only** authorization. WEB-02/03 backend isolation requires later real backend identity/tenant contracts and must not be claimed satisfied by a disconnected shell.

Exact direct and transitive npm dependencies are committed in `package-lock.json` (generated and validated from successful GitHub CI). CI uses `npm ci --ignore-scripts`; changes to dependency versions require an updated reviewed lockfile. No Stage/QA Gate, Production, or member launch.

Mandatory product member Web App is TRN-EP-13. This code implements the layout and safe baseline only; no Backend BFF or Production member authentication exists.

Privacy hardening: no third-party remote font, analytics or content dependencies. The UI uses local/system font fallbacks so loading the unauthenticated shell sends no data to external domains.
