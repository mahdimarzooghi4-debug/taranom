#!/usr/bin/env python3
"""CI-only HTTP integration checks for synthetic prescription-copy persistence."""
import base64
import hashlib
import json
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from uuid import uuid4

ROOT = "http://127.0.0.1:4320/internal/demo/v1/members"
MEMBER = "test:member-" + uuid4().hex
OTHER_MEMBER = "test:other-" + uuid4().hex
PRESCRIPTION = "test:prescription-" + uuid4().hex
URL = f"{ROOT}/{MEMBER}/prescriptions/{PRESCRIPTION}"


def call(url, data=None):
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    request = Request(url, data=json.dumps(data).encode() if data is not None else None,
                      headers=headers, method="POST" if data is not None else "GET")
    try:
        with urlopen(request, timeout=8) as response:
            return response.status, json.loads(response.read())
    except HTTPError as error:
        return error.code, json.loads(error.read())


def command(revision, payload, request_id=None):
    return {
        "requestId": request_id or str(uuid4()),
        "expectedRevision": revision,
        "memberRef": MEMBER,
        "prescriptionRef": PRESCRIPTION,
        "issuerRef": "test:issuer-ci",
        "sourceRevision": f"test:source-{revision + 1}",
        "issuedAt": "2026-10-28T00:00:00+00:00",
        "documentBase64": base64.b64encode(payload).decode(),
        "documentSha256": hashlib.sha256(payload).hexdigest(),
    }


def main():
    first_payload = b"synthetic prescription copy only"
    first = command(0, first_payload)
    status, result = call(URL, first)
    assert status == 200 and result["outcome"] == "RecordedCopy", result
    assert result["revision"] == 1 and result["clinicalUseAllowed"] is False

    status, replay = call(URL, first)
    assert status == 200 and replay["outcome"] == "Replayed", replay

    conflict = dict(first)
    conflict_payload = b"different synthetic bytes"
    conflict["documentBase64"] = base64.b64encode(conflict_payload).decode()
    conflict["documentSha256"] = hashlib.sha256(conflict_payload).hexdigest()
    status, result = call(URL, conflict)
    assert status == 409 and result["outcome"] == "ConflictingReplay", result

    stale = command(0, b"stale synthetic payload")
    status, result = call(URL, stale)
    assert status == 409 and result["outcome"] == "StaleRevision", result

    second = command(1, b"second synthetic copy")
    status, result = call(URL, second)
    assert status == 200 and result["revision"] == 2, result

    status, result = call(URL)
    assert status == 200 and result["versionCount"] == 2, result
    versions = result["versions"]
    assert base64.b64decode(versions[0]["documentBase64"]) == first_payload
    assert result["paymentAllowed"] is False and result["nationalReleaseAllowed"] is False

    status, result = call(f"{ROOT}/{OTHER_MEMBER}/prescriptions/{PRESCRIPTION}")
    assert status == 403 and result["error"] == "PRESCRIPTION_COPY_SCOPE_CONFLICT", result

    invalid = command(2, b"synthetic")
    invalid["extraAuthority"] = True
    status, _ = call(URL, invalid)
    assert status == 400

    live_url = f"{ROOT}/member-live/prescriptions/{PRESCRIPTION}"
    status, _ = call(live_url, command(2, b"synthetic"))
    assert status == 400

    print("PASS: synthetic prescription-copy HTTP journal, replay, revision, scope and authority-denial checks")


if __name__ == "__main__":
    main()
