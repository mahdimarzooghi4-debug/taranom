# Patient service navigation

`/patient-app/services` now uses Figma frame 118:2 instead of the legacy synthetic screen. The source account/treatment sections, six 350×60 rows, privacy note and existing exact local shell assets are retained. The source's provider-city/specialty subtitle is replaced with the actually supported center/capacity description. No unread badge is invented.

A shared typed service directory defines implemented navigation destinations and unavailable capabilities. Home tiles consume the same readiness values. This directory is not an authorization mechanism, grant registry, live uptime check or backend service discovery: each connected destination retains its real session/capability/publication gates. Membership, finance, smart guide, records/prescriptions and referral rows are disabled with explicit preparation status; no preview medical or financial data is substituted.

A compact expandable account/track section offers connected account, profile, appointments, notification and support routes. It extends the previously blank source space and expands vertically on demand. The connected home menu links to the full catalog. Legacy preview navigation performs a real page transition for this route so changing the URL cannot leave the sample component mounted.

There are no new API calls, database tables, permissions, external sends or authentication bypasses. This increment completes navigation readiness only; it does not implement the currently unavailable service backends. No APK, deployment or merge is performed.

Verification: member typecheck/build, browser navigation to the protected booking and notification routes with no session, absence of private requests in the catalog, disabled service readiness, legacy-to-home-to-catalog transition, 390px row/asset geometry and expanded 320px overflow checks. Existing home/account/discovery regression cases remain enabled.
