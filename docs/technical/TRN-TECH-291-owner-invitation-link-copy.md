# TRN-TECH-291 — Explicit clipboard copy for manually shared group invitation links

Status: DRAFT technical implementation contract derived from the existing TRN-TECH-285 manual share-link flow. No new Business decision, API, state, recipient identity or delivery channel.

## User need

The owner page creates a random, short-lived invitation link and displays it in a read-only text area. The current instruction tells the owner to copy the link, but offers no direct copy action. Add an explicit, accessible one-click copy control without changing the manual-share security model.

## Browser behavior

- Show the copy control only while an issued Pending link secret is held in the current component's memory and the existing owner page displays that link.
- Copy only after a direct owner click. Use `navigator.clipboard.writeText` with the exact displayed URL; never read the clipboard.
- On success, announce a concise Persian confirmation through an accessible live status.
- If Clipboard API is absent or rejects the write, keep the link visible, focus and select the existing read-only textarea, and announce that the owner must copy the selected text manually. Do not silently claim success.
- Reset the copy status whenever a new issue/retry operation clears or replaces the displayed link. A page reload continues to lose the in-memory secret as specified by TRN-TECH-285.
- Copy is local clipboard interaction only. It does not send a message, open a share target, contact a recipient, or call any backend endpoint.
- No clipboard read, browser storage, URL/query exposure, telemetry/logging of the secret, or automatic copy on render, issue, refresh or navigation.

## Security and compatibility

Keep the existing 256-bit random secret, fragment-only recipient URL, server-side SHA-256 hash, seven-day expiry, independent recipient authentication and explicit consent unchanged. Do not change issuance/replay/revocation/expiry contracts or expose additional receipt fields. The owner remains responsible for manually sharing the link with the intended person; the planning label does not verify recipient identity.

## Verification

Real Chromium tests must prove:
1. No clipboard write occurs before an explicit click.
2. Explicit click writes the exact currently displayed fragment URL and announces success.
3. Clipboard denial/unavailability keeps the link visible, selects the complete textarea value, and announces manual-copy fallback without claiming success.
4. Copying causes no additional API request or invitation mutation, does not change the invitation reference, and writes nothing to local/session storage.
5. A replacement issuance clears prior copy status and only the new current link is copyable.
6. Existing lost-ACK exact-request recovery, recipient consent, link secrecy, RTL and 390/320px layout remain green.

Run Figma Appointment Integration 2/2 and the canonical mainline regression 14/14 on the exact final SHA, then record a technical COMMENTED review. Keep the new PR Draft/Open/Unmerged stacked on PR #294. No Ready for Review, merge, Stage, formal QA Gate, Release Approval or Production. National release remains DENIED_BY_DEFAULT; all 17 launch domains and 19 external evidence dependencies remain unresolved.
