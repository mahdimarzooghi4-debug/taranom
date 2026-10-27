#!/usr/bin/env python3
"""Real HTTP -> ASP.NET -> isolated PostgreSQL synthetic appointment request."""
import json
import sys
from uuid import uuid4
from urllib.error import HTTPError
from urllib.request import Request, urlopen

BASE = "http://127.0.0.1:4320/internal/demo/v1/appointment-requests/"


def call(request_ref, data=None, origin=None):
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    if origin:
        headers["Origin"] = origin
    request = Request(
        BASE + request_ref,
        method="POST" if data is not None else "GET",
        data=json.dumps(data).encode() if data is not None else None,
        headers=headers,
    )
    try:
        with urlopen(request, timeout=6) as response:
            return response.status, json.loads(response.read())
    except HTTPError as error:
        return error.code, json.loads(error.read())


def main():
    request_ref = "test:appointment-" + uuid4().hex
    request_id = str(uuid4())
    body = {
        "requestId": request_id,
        "expectedRevision": 0,
        "memberRef": "test:member-" + uuid4().hex,
        "centerRef": "test:center-" + uuid4().hex,
        "departmentRef": "test:department-" + uuid4().hex,
        "requestedDoctorRef": "test:doctor-" + uuid4().hex,
        "slotRef": "test:slot-" + uuid4().hex,
    }

    code, created = call(request_ref, body)
    assert code == 200 and created["outcome"] == "RecordedRequest"
    assert created["revision"] == 1 and created["requestCount"] == 1
    assert created["requests"][0]["command"]["requestedDoctorRef"] == body["requestedDoctorRef"]
    for flag in (
        "memberAuthenticated", "centerAuthenticated", "requestedDoctorLicensed",
        "slotAvailabilityVerified", "bookingConfirmed", "clinicalActionAllowed",
        "nationalReleaseAllowed",
    ):
        assert created[flag] is False, flag

    code, replayed = call(request_ref, body)
    assert code == 200 and replayed["outcome"] == "Replayed"
    assert replayed["revision"] == 1 and replayed["requestCount"] == 1

    conflict = dict(body, requestedDoctorRef="test:other-doctor-" + uuid4().hex)
    code, rejected = call(request_ref, conflict)
    assert code == 409 and rejected["outcome"] == "ConflictingReplay"

    stale = dict(body, requestId=str(uuid4()))
    code, rejected = call(request_ref, stale)
    assert code == 409 and rejected["outcome"] == "StaleRevision"

    code, persisted = call(request_ref)
    assert code == 200 and persisted["outcome"] == "Read"
    assert persisted["revision"] == 1 and persisted["requests"] == created["requests"]

    code, _ = call("prod:real-reference", body)
    assert code == 400
    code, _ = call("test:origin-denied-" + uuid4().hex, body, "https://attacker.invalid")
    assert code == 403
    malformed = dict(body, diagnosis="must-not-be-accepted")
    code, _ = call("test:extra-field-" + uuid4().hex, malformed)
    assert code == 400

    print("PASS: synthetic request intent persisted and verified through HTTP/PostgreSQL")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc(file=sys.stderr)
        print("DENIED: SYNTHETIC_APPOINTMENT_REQUEST_HTTP_FAILED", file=sys.stderr)
        raise SystemExit(1)
