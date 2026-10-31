#!/usr/bin/env python3
"""Real HTTP -> ASP.NET -> isolated PostgreSQL synthetic appointment request."""
import json
import sys
from datetime import datetime
from uuid import uuid4
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from urllib.parse import urlencode

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


def read_schedule(url):
    request = Request(url, method="GET", headers={"Accept": "application/json"})
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
        "unverifiedScheduleSelection": {
            "sourceRef": "test:schedule-source-" + uuid4().hex,
            "sourceRevision": "test:schedule-revision-7",
            "observedAt": "2026-10-27T09:00:00Z",
            "slotRef": "test:slot-placeholder",
            "startsAt": "2026-10-28T10:00:00Z",
            "endsAt": "2026-10-28T10:30:00Z",
        },
    }
    body["unverifiedScheduleSelection"]["slotRef"] = body["slotRef"]

    code, created = call(request_ref, body)
    assert code == 200 and created["outcome"] == "RecordedRequest"
    assert created["revision"] == 1 and created["requestCount"] == 1
    assert created["requests"][0]["command"]["requestedDoctorRef"] == body["requestedDoctorRef"]
    returned_selection = created["requests"][0]["command"]["unverifiedScheduleSelection"]
    expected_selection = body["unverifiedScheduleSelection"]
    assert returned_selection["sourceRef"] == expected_selection["sourceRef"]
    assert returned_selection["sourceRevision"] == expected_selection["sourceRevision"]
    assert returned_selection["slotRef"] == body["slotRef"] == expected_selection["slotRef"]
    for timestamp in ("observedAt", "startsAt", "endsAt"):
        returned_at = datetime.fromisoformat(
            returned_selection[timestamp].replace("Z", "+00:00"))
        expected_at = datetime.fromisoformat(
            expected_selection[timestamp].replace("Z", "+00:00"))
        assert returned_at == expected_at, timestamp
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

    schedule_base = (
        "http://127.0.0.1:4320/internal/demo/v1/centers/"
        + body["centerRef"] + "/appointment-schedule"
    )
    schedule_query = urlencode([
        ("departmentRef", body["departmentRef"]),
        ("requestedDoctorRef", body["requestedDoctorRef"]),
        ("from", "2026-10-28T03:30:00+03:30"),
        ("until", "2026-10-29T03:30:00+03:30"),
    ])
    schedule_url = schedule_base + "?" + schedule_query
    schedule_code, schedule = read_schedule(schedule_url)
    assert schedule_code == 200 and schedule["contract"] == "TRN-TECH-229"
    assert schedule["outcome"] == "SourceNotConfigured"
    assert schedule["centerRef"] == body["centerRef"]
    assert schedule["departmentRefFilter"] == body["departmentRef"]
    assert schedule["requestedDoctorRefFilter"] == body["requestedDoctorRef"]
    assert schedule["from"] == "2026-10-28T03:30:00.0000000+03:30"
    assert schedule["until"] == "2026-10-29T03:30:00.0000000+03:30"
    assert schedule["sourceClaims"] == []
    assert schedule["unverifiedSourceMetadata"] is None
    for flag in (
        "sourceAuthenticated", "slotAvailabilityVerified", "bookingConfirmed",
        "clinicalActionAllowed", "nationalReleaseAllowed",
    ):
        assert schedule[flag] is False, flag

    code, rejected = read_schedule(schedule_base + "?unexpected=value")
    assert code == 400 and rejected["outcome"] == "INVALID_TEST_QUERY"
    duplicate = (
        schedule_base + "?departmentRef=" + body["departmentRef"]
        + "&departmentRef=" + body["departmentRef"]
    )
    code, rejected = read_schedule(duplicate)
    assert code == 400 and rejected["outcome"] == "INVALID_TEST_QUERY"

    code, _ = read_schedule(schedule_base + "?from=2026-10-28T00:00:00Z")
    assert code == 400
    code, _ = read_schedule(schedule_base +
        "?from=2026-10-28T00:00:00&until=2026-10-29T00:00:00Z")
    assert code == 400
    code, _ = read_schedule(schedule_base +
        "?from=2026-10-29T00:00:00Z&until=2026-10-28T00:00:00Z")
    assert code == 400

    for invalid_from in (
        "2026/10/28 00:00:00Z",
        "2026-10-28T00:00:00.12345678Z",
        "2026-02-30T00:00:00Z",
    ):
        code, _ = read_schedule(schedule_base + "?" + urlencode([
            ("from", invalid_from),
            ("until", "2026-10-29T00:00:00Z"),
        ]))
        assert code == 400, invalid_from

    null_body = dict(body, requestId=str(uuid4()), expectedRevision=0,
                     unverifiedScheduleSelection=None)
    code, null_receipt = call("test:null-selection-" + uuid4().hex, null_body)
    assert code == 200 and null_receipt["requests"][0]["command"]["unverifiedScheduleSelection"] is None
    nested_extra = dict(body, requestId=str(uuid4()), expectedRevision=0,
                        unverifiedScheduleSelection=dict(
                            body["unverifiedScheduleSelection"], approved=True))
    code, _ = call("test:nested-extra-" + uuid4().hex, nested_extra)
    assert code == 400
    for invalid_timestamp in (
        "2026-10-27T09:00:00",
        "2026-10-27 09:00:00Z",
        "2026-02-30T09:00:00Z",
        "2026-10-27T09:00:00.12345678Z",
    ):
        invalid_selection = dict(
            body["unverifiedScheduleSelection"], observedAt=invalid_timestamp)
        invalid_body = dict(
            body, requestId=str(uuid4()), expectedRevision=0,
            unverifiedScheduleSelection=invalid_selection)
        code, _ = call("test:invalid-provenance-time-" + uuid4().hex, invalid_body)
        assert code == 400, invalid_timestamp

    print("PASS: synthetic unverified schedule provenance persisted; timestamp syntax validated over HTTP/PostgreSQL")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc(file=sys.stderr)
        print("DENIED: SYNTHETIC_APPOINTMENT_REQUEST_HTTP_FAILED", file=sys.stderr)
        raise SystemExit(1)
