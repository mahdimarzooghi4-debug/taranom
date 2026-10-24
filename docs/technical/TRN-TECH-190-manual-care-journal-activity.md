# TRN-TECH-190 — DRAFT / Manual cross-workspace care journal activity reconciliation
Date: 2026-10-21. Business TRN-BIZ-046 (coordination responsibility, no clinical authority), independent TRN-BIZ-035/036/043. Existing technical TRN-TECH-115/184/185/186/187/188/189.

## Gap and scoped contract
Member and Center currently read the same immutable synthetic PostgreSQL coordination journal and perform independent manually selected claim writes. A member or center viewing a case cannot distinguish new follow-up claims since that browser's last source read without comparing the entire event history by hand. Add explicit human-clicked **manual refresh of one already source-read `test:` case** on both existing panels; render only genuinely appended unverified claims since the **previous read of that same browser page**.

Each refresh calls only the existing canonical journal GET, requires existing full snapshot contract/13-denial validation and same caseRef, then checks that the newly returned revision never goes backward and ALL previously read event fields (revision, domain action, UUID, member/root lineage, claimed actor and note/source ref) remain byte-identical in semantic fields. If previous event is rewritten, removed, reordered, snapshot is malformed, or HTTP fails: refuse the whole new state and remove any purported activity/success UI; never silently accept a replacement history. If same revision, show zero additions. If new appended events, show their exact source action/revision/reference without inferring validity, contact delivery, consent, clinical review, appointment or status. Changing selected case invalidates activity. A successful local POST/ACK or imported recovery is not interpreted as an external arrival. A refresh is explicitly manual with zero POST/auto-retry; no browser storage, websocket, event subscription, notification/read/unread state, priority score or backend endpoint.

## Testable acceptance
Four real Chromium/Kestrel/isolated PostgreSQL cases:
1. Member reads case, Center manually commits an unverified contact, Member manual refresh sees only the exact appended contact; zero additional POST, second refresh shows zero.
2. Center reads case, Member manually commits a source claim, Center explicit refresh shows exact source; zero POST, second refresh zero.
3. Forge GET containing historically mutated root UUID with a self-consistent newer row: reject and hide activity, preserving canonical source.
4. Regress the revision to a previously valid snapshot: reject without action or false activity.
Run canonical 14-job full regression, complete preliminary COMMENTED technical Code Review only. All fixtures strictly ephemeral `test:`; no real identity/member consent, provider/clinical authority, service delivery, patient record or paid operations. Official nationwide first launch remains DENIED_BY_DEFAULT. PR stays Draft/Open/Unmerged; no Stage/QA Gate/Release/Production.
