# Taranom Member Web — Sprint 02 authorized code slice

**Scope:** Persian RTL accessible React/TypeScript **member app shell only**. No real user login, account, credit values, clinical records, insurer/provider integrations, payment, AI or synthetic fake real-world authority.

- `npm install --no-audit --no-fund`
- `npm run typecheck`
- `npm run build`
- `npx playwright install chromium` then `npm run test:e2e`

The app intentionally renders every member section in an explicitly unavailable state. Navigation paths are browser routes **only** and do not imply existing .NET read endpoints. It never sends private network API requests or persists sensitive state in browser storage. Pages and tests cover WEB-01, WEB-04…12 under this **shell-only** authorization. WEB-02/03 backend isolation requires later real backend identity/tenant contracts and must not be claimed satisfied by a disconnected shell.

CI presently installs exact top-level npm package versions; **transitive dependency lockfile is not yet committed** and remains a reproducibility gate until generated and verified. No Stage/QA Gate, Production, or member launch.

Mandatory product member Web App is TRN-EP-13. This code implements the layout and safe baseline only; no Backend BFF or Production member authentication exists.
