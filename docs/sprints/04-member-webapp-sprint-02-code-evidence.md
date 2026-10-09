# TRN-SP-02 — Member Web App first authorized code slice

- Owner Code Authorization: **2026-10-09**; strictly member web **shell** in React/TypeScript, Persian RTL, navigation, accessible and disconnected states, real Chromium browser tests.
- **No real member login**, no generated user name/session, no fabricated wallet/credit, no clinical record/diagnosis, insurance determination, payments, providers, or real API route integration.
- Implementation: `apps/member-web` with React 19.2.0, React DOM 19.2.0, TypeScript 5.9.3, Vite 7.1.9 and Playwright 1.56.1. Candidate frontend package choices; CI supplies execution evidence.
- Navigation: nine browser-only routes per `TRN-TECH-011`, including the optional unauthorized-path fallback. The shell presents no authenticated profile data and shows `NOT_CONNECTED` as a textual, accessible state.
- Security: no API fetch, no browser storage of tokens/PHI, no login simulation, no synthetic principal imported from Sprint 01, no real Backend routes. Member identifiers cannot be provided by query string to establish identity.
- CI: GitHub Actions exact HEAD with pinned official Action commit SHAs, Node 22, TypeScript check, Vite production bundle, real Chromium Playwright tests, XML artifact, and static source checks against forbidden state/adapters.
- WEB-01, WEB-04…12 are relevant disconnected-shell tests; WEB-02/03 production backend scope/tenant verification **cannot be honestly passed without an authorized real IdP/BFF and backend contracts**.
- **Open supply-chain gate:** initial npm dependencies are exact direct versions but transitive npm lockfile is not committed yet. Must resolve before release-readiness claim.
- Code Review and any Stage/QA/Release/Production require separate approval; Sprint 01 Code Review itself is not independently accepted and remains Draft/Open.
