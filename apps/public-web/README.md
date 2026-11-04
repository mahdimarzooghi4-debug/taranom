# Taranom Public Website

Persian RTL introduction and membership-awareness website adapted from the approved Figma landing page. The site is informational: it does not search providers, book appointments, collect medical data, create accounts, calculate membership fees, process payments, or expose member services.

## Local preview

```sh
npm ci --no-audit --no-fund --ignore-scripts
npm run dev
```

The membership CTA can hand off to a configured member application using `VITE_TARANOM_MEMBER_APP_URL`. Without that setting it scrolls to the membership explanation instead of implying that registration is available.

## Backend status

The current repository has no public membership registration endpoint, production member authentication, or verified payment/identity integration. The existing member web application is explicitly a disconnected shell. This landing page therefore makes no API calls and collects no personal data. Real registration must wait for approved identity, subscription payer, payment, legal-basis, account-activation, and member-authentication contracts in the backend.

No Stage, QA acceptance, release, or Production deployment is implied by this preview.