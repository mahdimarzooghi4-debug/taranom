#!/usr/bin/env python3
"""TRN-TECH-129: live loopback .NET + restricted PostgreSQL case worklist."""
import json
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from uuid import uuid4

HOST = "http://127.0.0.1:4320"
ZERO = "00000000-0000-0000-0000-000000000000"


def send(path, body=None, extra=None):
    payload = None if body is None else json.dumps(body).encode()
    headers = {"Accept": "application/json", **(extra or {})}
    if payload is not None:
        headers["Content-Type"] = "application/json"
    request = Request(HOST + path, data=payload, headers=headers)
    try:
        with urlopen(request, timeout=10) as response:
            return response.status, json.loads(response.read())
    except HTTPError as exc:
        return exc.code, json.loads(exc.read())


def command(case, revision, first_id):
    first = revision == 0
    return {
        "requestId": str(uuid4()),
        "expectedRevision": revision,
        "action": "SUBMIT_UNVERIFIED" if first else "FOLLOW_UP_UNVERIFIED",
        "memberRef": "test:member-private",
        "topic": "MEDICAL_SERVICE_COMPLAINT",
        "unverifiedEvidenceRef": "test:private-evidence",
        "claimedSubmitterRef": "test:private-submitter",
        "originalSubmissionRequestId": ZERO if first else first_id,
        "claimedFollowUpActorRef": None if first else "test:private-actor",
        "unverifiedFollowUpNoteRef": None if first else "test:private-note",
    }


def main():
    marker = "test:wl-" + uuid4().hex
    a, b = marker + "-a", marker + "-b"
    first_id = None
    for case, revisions in ((a, 1), (b, 2)):
        for rev in range(revisions):
            payload = command(case, rev, first_id)
            if rev == 0:
                first_id = payload["requestId"]
            status, saved = send("/internal/demo/v1/support/" + case + "/commands", payload)
            assert status == 200 and saved["revision"] == rev + 1, (status, saved)
        first_id = None

    base = "/internal/demo/v1/support-worklist?afterCaseRef="
    status, first = send(base + marker + "&limit=1")
    assert status == 200 and first["contract"] == "TRN-TECH-129", (status, first)
    assert first["source"] == "VERIFIED_ISOLATED_POSTGRES_SUPPORT_JOURNAL"
    assert first["cases"] == [{"caseRef": a, "revision": 1, "followUpCount": 0}]
    assert first["hasMore"] is True and first["nextAfterCaseRef"] == a
    for flag in ("memberAuthenticated", "complaintLegallySubmitted",
                 "insurerContacted", "clinicalReviewCompleted",
                 "nationalReleaseAllowed"):
        assert first[flag] is False

    status, second = send(base + a + "&limit=1")
    assert status == 200 and second["cases"] == [
        {"caseRef": b, "revision": 2, "followUpCount": 1}
    ], (status, second)
    assert second["hasMore"] is False and second["nextAfterCaseRef"] is None
    assert "test:private-evidence" not in json.dumps(second)
    assert "test:member-private" not in json.dumps(second)

    status, after = send(base + b + "&limit=1")
    assert status == 200 and after["cases"] == []
    assert after["hasMore"] is False

    invalid = [
        "/internal/demo/v1/support-worklist",
        "/internal/demo/v1/support-worklist?limit=1",
        base + marker + "&limit=0",
        base + marker + "&limit=11",
        base + marker + "&limit=01",
        base + "real-member&limit=1",
        base + marker + "&limit=1&role=admin",
        base + marker + "&afterCaseRef=" + a + "&limit=1",
    ]
    for uri in invalid:
        status, denied = send(uri)
        assert status == 400 and denied["nationalReleaseAllowed"] is False, uri
    status, denied = send(base + marker + "&limit=1",
                          extra={"Host": "outside.invalid:4320"})
    assert status == 403 and denied["nationalReleaseAllowed"] is False

    print("PASS: bounded MVCC+audit-verified support worklist, real PostgreSQL reader, safe pagination")


if __name__ == "__main__":
    main()
