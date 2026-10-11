#!/usr/bin/env python3
"""Real HTTP -> ASP.NET -> ephemeral PostgreSQL center journal acceptance."""
import json
import sys
from uuid import uuid4
from urllib.error import HTTPError
from urllib.request import Request, urlopen

BASE = "http://127.0.0.1:4320/internal/demo/v1/centers/"


def call(path, data=None, origin=None):
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    if origin:
        headers["Origin"] = origin
    request = Request(BASE + path, method="POST" if data is not None else "GET",
                      data=json.dumps(data).encode() if data is not None else None,
                      headers=headers)
    try:
        with urlopen(request, timeout=6) as response:
            return response.status, response.headers, json.loads(response.read())
    except HTTPError as error:
        return error.code, error.headers, json.loads(error.read())


def command(version, kind, department=None, doctor=None, request=None):
    return {
        "requestId": request or str(uuid4()), "expectedVersion": version,
        "kind": kind, "departmentRef": department, "doctorRef": doctor
    }


def main():
    case = "test:center-http-" + uuid4().hex
    dept = "test:dep-" + uuid4().hex
    doctor = "test:doctor-" + uuid4().hex
    code, headers, result = call(case)
    assert code == 200 and result["currentVersion"] == 0
    assert result["departments"] == [] and result["doctors"] == []
    assert result["syntheticOnly"] is True
    assert result["managerAuthenticated"] is False
    assert result["professionalQualificationVerified"] is False
    assert result["nationalReleaseAllowed"] is False
    assert headers.get("Cache-Control") == "no-store"
    assert "Access-Control-Allow-Origin" not in headers

    first = command(0, "DEFINE_DEPARTMENT", department=dept)
    code, _, state = call(case + "/commands", first)
    assert code == 200 and state["outcome"] == "Applied" and state["currentVersion"] == 1
    code, _, replay = call(case + "/commands", first)
    assert code == 200 and replay["outcome"] == "Replayed" and replay["currentVersion"] == 1
    collision = dict(first, departmentRef="test:unexpected")
    code, _, conflict = call(case + "/commands", collision)
    assert code == 409 and conflict["outcome"] == "ConflictingReplay"
    code, _, stale = call(case + "/commands", command(0, "RECORD_DOCTOR", doctor=doctor))
    assert code == 409 and stale["outcome"] == "StaleRevision"

    for expected, kind, d, doc in (
        (1, "RECORD_DOCTOR", None, doctor),
        (2, "ASSIGN_DOCTOR", dept, doctor),
        (3, "END_DOCTOR", None, doctor),
    ):
        code, _, state = call(case + "/commands", command(expected, kind, d, doc))
        assert code == 200 and state["outcome"] == "Applied"
        assert state["currentVersion"] == expected + 1
    code, _, state = call(case)
    assert code == 200 and state["currentVersion"] == 4 and state["eventCount"] == 4
    assert state["departments"] == [dept]
    assert state["doctors"][0]["affiliation"] == "Ended"
    assert state["doctors"][0]["departmentRefs"] == []

    code, _, second = call("test:independent-" + uuid4().hex)
    assert code == 200 and second["currentVersion"] == 0
    code, _, invalid = call("real:center")
    assert code == 400 and invalid["nationalReleaseAllowed"] is False
    code, _, bad = call(case + "/commands",
                         dict(command(4, "DEFINE_DEPARTMENT", department="test:other"),
                              memberId="test:spoof"))
    assert code == 400 and bad["nationalReleaseAllowed"] is False
    code, _, denied = call(case + "/commands",
                           command(4, "DEFINE_DEPARTMENT", department="test:other"),
                           origin="https://untrusted.example")
    assert code == 403 and denied["managerAuthenticated"] is False
    code, _, after = call(case)
    assert code == 200 and after["currentVersion"] == 4

    # New read-only center history supports deterministic cursor pagination
    # and reconstructs the verified event stream at exact past versions.
    code, _, first_page = call(case + "/history?afterVersion=0&limit=2")
    assert code == 200 and first_page["currentVersion"] == 4
    assert first_page["syntheticOnly"] and not first_page["managerAuthenticated"]
    assert not first_page["professionalQualificationVerified"]
    assert not first_page["nationalReleaseAllowed"]
    assert [x["version"] for x in first_page["events"]] == [1, 2]
    assert first_page["hasMore"] is True and first_page["nextAfterVersion"] == 2
    code, _, last_page = call(case + "/history?afterVersion=2&limit=2")
    assert code == 200
    assert [x["version"] for x in last_page["events"]] == [3, 4]
    assert last_page["hasMore"] is False and last_page["nextAfterVersion"] is None
    assert all(x["hypotheticalOnly"] for x in first_page["events"] + last_page["events"])
    assert [x["kind"] for x in first_page["events"] + last_page["events"]] == [
        "DefineDepartment", "RecordDoctorAffiliation",
        "AssignDoctorToDepartment", "EndDoctorAffiliation"
    ]
    for version, expected_affiliation, expected_assignments in (
        (0, None, []),
        (2, "PendingIndependentVerification", []),
        (3, "PendingIndependentVerification", [dept]),
        (4, "Ended", []),
    ):
        code, _, past = call(case + "/versions/" + str(version))
        assert code == 200 and past["asOfVersion"] == version
        assert past["currentVersion"] == 4 and past["syntheticOnly"]
        assert past["managerAuthenticated"] is False and past["nationalReleaseAllowed"] is False
        assert past["snapshot"]["currentVersion"] == version
        if expected_affiliation is None:
            assert past["snapshot"]["doctors"] == [] and past["snapshot"]["departments"] == []
        else:
            assert past["snapshot"]["doctors"][0]["affiliation"] == expected_affiliation
            assert past["snapshot"]["doctors"][0]["departmentRefs"] == expected_assignments
    for suffix in (
        "/history?afterVersion=-1&limit=2",
        "/history?afterVersion=0&limit=21",
        "/history?afterVersion=0&limit=2&manager=true",
        "/history?afterVersion=99&limit=2",
        "/history?afterVersion=0&afterVersion=1&limit=2",
        "/versions/-1", "/versions/5", "/versions/1?status=approved",
    ):
        code, _, invalid = call(case + suffix)
        assert code == 400 and invalid["nationalReleaseAllowed"] is False
        assert invalid["managerAuthenticated"] is False
    code, _, fresh = call("test:isolated-history-" + uuid4().hex +
                         "/history?afterVersion=0&limit=2")
    assert code == 200 and fresh["currentVersion"] == 0 and fresh["events"] == []
    code, _, latest = call(case)
    assert code == 200 and latest["currentVersion"] == 4
    print("PASS: immutable cursor history, exact-version reconstructed center state, no cross-center data")
    print("PASS: actual ASP.NET/PostgreSQL journal 4-step center case, replay, stale and fail-close")




def support_call(case_ref, command_data=None, origin=None):
    address = "http://127.0.0.1:4320/internal/demo/v1/support/" + case_ref
    headers = {"Accept": "application/json"}
    if command_data is not None:
        address += "/commands"
        headers["Content-Type"] = "application/json"
    if origin:
        headers["Origin"] = origin
    request = Request(address, method="POST" if command_data else "GET",
                      data=json.dumps(command_data).encode() if command_data else None,
                      headers=headers)
    try:
        with urlopen(request, timeout=8) as response:
            return response.status, json.loads(response.read())
    except HTTPError as error:
        return error.code, json.loads(error.read())


def support_payload(case_ref, member, evidence, submitter, req_id,
                    revision=0, action="SUBMIT_UNVERIFIED",
                    topic="MEDICAL_SERVICE_COMPLAINT", original=None,
                    actor=None, note=None):
    return {"requestId": req_id, "expectedRevision": revision,
            "action": action, "memberRef": member, "topic": topic,
            "unverifiedEvidenceRef": evidence, "claimedSubmitterRef": submitter,
            "originalSubmissionRequestId": original or "00000000-0000-0000-0000-000000000000",
            "claimedFollowUpActorRef": actor, "unverifiedFollowUpNoteRef": note}


def verify_support():
    case_ref = "test:support-" + uuid4().hex
    member, evidence, submitter = ["test:" + uuid4().hex for _ in range(3)]
    code, empty = support_call(case_ref)
    assert code == 200 and empty["revision"] == 0 and empty["followUpCount"] == 0
    assert empty["memberAuthenticated"] is False
    assert empty["complaintLegallySubmitted"] is False
    assert empty["insurerContacted"] is False
    assert empty["clinicalReviewCompleted"] is False
    assert empty["nationalReleaseAllowed"] is False

    initial_id = str(uuid4())
    original = support_payload(case_ref, member, evidence, submitter, initial_id)
    code, created = support_call(case_ref, original)
    assert code == 200 and created["revision"] == 1
    assert created["outcome"] == "RecordedUnverified"
    assert created["topic"] == "MedicalServiceComplaint"
    assert created["originalRequestId"] == initial_id
    code, replay = support_call(case_ref, original)
    assert code == 200 and replay["outcome"] == "Replayed" and replay["revision"] == 1
    code, collision = support_call(case_ref, dict(original, memberRef="test:different"))
    assert code == 409 and collision["outcome"] == "ConflictingReplay"
    code, stale = support_call(case_ref, dict(original, requestId=str(uuid4())))
    assert code == 409 and stale["outcome"] == "StaleRevision"

    follow_id = str(uuid4())
    note = "test:note-" + uuid4().hex
    follow = support_payload(case_ref, member, evidence, submitter, follow_id, 1,
                             "FOLLOW_UP_UNVERIFIED", original=initial_id,
                             actor="test:claimed-actor", note=note)
    code, logged = support_call(case_ref, follow)
    assert code == 200 and logged["revision"] == 2 and logged["followUpCount"] == 1
    assert logged["followUps"][0]["noteRef"] == note
    code, after = support_call(case_ref)
    assert code == 200 and after["revision"] == 2
    assert after["followUps"][0]["revision"] == 2
    code, wrong_topic = support_call(case_ref, dict(
        follow, requestId=str(uuid4()), expectedRevision=2,
        topic="INSURANCE_COVERAGE_DISPUTE"))
    assert code == 409 and wrong_topic["outcome"] == "ScopeMismatch"
    code, rejected = support_call(case_ref, dict(follow, requestId=str(uuid4()),
                                                  expectedRevision=2, clinicalApproved=True))
    assert code == 400 and rejected["clinicalReviewCompleted"] is False
    code, origin_denied = support_call(case_ref, dict(follow, requestId=str(uuid4()),
                                                      expectedRevision=2),
                                       origin="https://external.invalid")
    assert code == 403 and origin_denied["nationalReleaseAllowed"] is False
    code, real_denied = support_call("real:complaint")
    assert code == 400 and real_denied["memberAuthenticated"] is False
    code, independent = support_call("test:independent-" + uuid4().hex)
    assert code == 200 and independent["revision"] == 0
    code, final = support_call(case_ref)
    assert code == 200 and final["revision"] == 2
    print("PASS: canonical synthetic support submit/followup persisted, replay-safe, rights denied")


if __name__ == "__main__":
    try:
        main()
        verify_support()
    except Exception:
        print("DENIED: EPHEMERAL_CENTER_HTTP_INTEGRATION_FAILED", file=sys.stderr)
        raise SystemExit(1)
