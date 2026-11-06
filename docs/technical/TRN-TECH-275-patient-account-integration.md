# Patient account integration

Figma: file 78mOnfH4h9AX5Jk2ufRn0U, account frame 38:88 (390×844).
Route: `/patient-app/account`.

The account summary reads the existing GET `/api/v1/me/profile` using the host-supplied bearer session. Name comes exclusively from that owner-scoped response, not from Figma examples or query-string identity selectors. Self-declared profile data does not prove membership, verified identity or eligibility. A new account displays an explicit missing-name state and a link to the connected personal-information form. Reloads clear prior profile data, abort superseded reads, handle invalid responses, and permit retry after denial.

Navigation from preview screens now performs a full navigation for account, as it already does for connected appointment/profile/support routes. Without this change the preview component could retain its sample account after the URL changed. Account links open connected personal info and support routes. Bottom navigation reuses the existing account-active shell.

Account summary, membership explanation, group explanation and settings follow the Figma card order, geometry, colors and typography. The avatar is the downloaded 58×58 SVG from node 38:172; logo, bell and four navigation assets are exact existing local matches. Refresh, missing-data and unavailable-service states extend the source design. No unread badge or fabricated phone, ID, membership status, fee or group count is displayed. Consent/privacy actions and logout stay visibly unavailable until their real service/session integration exists; no simulated consent changes or token-only logout is introduced.

Verification: member TypeScript/build checks, browser cases for authenticated profile reading, navigation to connected profile/support, missing session without an API request, empty and malformed profiles, stale-data removal on denied refresh, recovery, preview-to-connected routing, and 390px geometry/local asset loading. Existing profile backend persistence is unchanged and covered by the PostgreSQL workflow. No deployment, merge, payment, identity verification or membership activation is performed.
