#!/usr/bin/env python3
"""TRN-TECH-094: real opt-in loopback HTTP -> canonical recorder + continuity."""
import copy
import json
import uuid
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from test_synthetic_cost_http import body, claim

URL = "http://127.0.0.1:4318/internal/ops/v1/synthetic-cost-choice"
ORIGIN = "http://127.0.0.1:4318"


def send(data, headers=None, raw=None, method="POST"):
    payload = json.dumps(data).encode() if raw is None else raw
    req = Request(URL, data=payload if method == "POST" else None,
                  method=method,headers={
                      "Content-Type": "application/json", "Origin":ORIGIN,
                      **(headers or {})})
    try:
        with urlopen(req, timeout=8) as res:
            return res.status, json.loads(res.read())
    except HTTPError as ex:
        text = ex.read()
        return ex.code, json.loads(text) if text.strip() else {}


def assert_denied(d):
    for key in ("memberAuthenticated", "choiceCarriedForward",
                "financialCommitmentCreated", "clinicalConsentGranted",
                "walletMutated", "nationalReleaseAllowed",
                "officialChoiceRecorded", "choiceSignatureValid"):
        assert d[key] is False, (key, d)


def command(p, fingerprint, choice="AcknowledgedUncertainty", rev=0):
    return dict(requestId=str(uuid.uuid4()), expectedRevision=rev,
                memberRef=p["memberRef"], previewId=p["previewId"],
                previewRevision=p["previewRevision"],
                previewFingerprint=fingerprint, choice=choice)


def run():
    from test_synthetic_cost_http import send as cost_send
    previous = body()
    status, preview, _ = cost_send(previous)
    assert status == 200 and preview["state"] == "MissingOrIncomplete"
    base = dict(previous=previous,current=copy.deepcopy(previous),
                history=[],attempt=None)
    code, value = send(base)
    assert code == 200 and value["outcome"] == "InspectionOnly"
    assert value["continuity"] == "NoPriorChoiceToCarry"
    assert value["historyRevision"] == 0
    assert_denied(value)
    original = command(previous,preview["fingerprint"])
    base["attempt"] = original
    code, value = send(base)
    assert code == 200 and value["outcome"] == "RecordedUnverified", value
    assert value["historyRevision"] == 1
    assert value["continuity"] == "UnverifiedSamePreviewOnly"
    assert value["lastUnverifiedChoice"] == "AcknowledgedUncertainty"
    assert_denied(value)

    base["history"] = [original]
    code, replay = send(base)
    assert code == 200 and replay["outcome"] == "Replayed"
    assert replay["historyRevision"] == 1
    assert_denied(replay)
    changed = dict(original, choice="Deferred")
    base["attempt"] = changed
    code, conflict = send(base)
    assert code == 200 and conflict["outcome"] == "ConflictingReplay"
    assert conflict["historyRevision"] == 1
    base["attempt"] = command(previous,preview["fingerprint"],rev=0)
    assert send(base)[1]["outcome"] == "StaleRevision"
    base["attempt"] = command(previous,"0" * 64,rev=1)
    assert send(base)[1]["outcome"] == "PreviewChanged"
    base["attempt"] = command(previous,preview["fingerprint"],rev=1)
    base["attempt"]["memberRef"] = "test:other-member"
    assert send(base)[1]["outcome"] == "ScopeMismatch"

    base["attempt"] = None
    base["current"]["previewRevision"] = 2
    code, revised = send(base)
    # Existing domain distinguishes missing evidence from otherwise complete revision changes.
    assert code == 200 and revised["continuity"] == "CurrentMissingRequiresSeparateReview"
    assert revised["changedComponents"] == []
    assert_denied(revised)
    base["attempt"] = command(previous,preview["fingerprint"],rev=1)
    code, rejected = send(base)
    assert code == 200 and rejected["outcome"] == "PreviewChanged"
    assert rejected["historyRevision"] == 1
    base["attempt"] = None
    base["current"]["lines"][0]["state"] = "ProvisionalClaim"
    claim(base["current"]["lines"][0])
    assert send(base)[1]["continuity"] == "CurrentMissingRequiresSeparateReview"
    base["current"]["lines"][0].update(state="ConflictingClaims",amount=None,currency=None)
    assert send(base)[1]["continuity"] == "CurrentConflictRequiresSeparateReview"
    # A fully provisional (still UNVERIFIED) five-lane estimate is the
    # distinct "changed preview" path when there are no missing sources.
    complete = body()
    for line in complete["lines"]:
        claim(line)
    full_preview = cost_send(complete)[1]
    full_command = command(complete, full_preview["fingerprint"])
    full_case = dict(previous=complete,current=copy.deepcopy(complete),
                     history=[],attempt=full_command)
    assert send(full_case)[1]["outcome"] == "RecordedUnverified"
    full_case["history"] = [full_command]
    full_case["attempt"] = None
    full_case["current"]["previewRevision"] = 2
    code, changed_preview = send(full_case)
    assert code == 200 and changed_preview["continuity"] == "ChangedPreviewRequiresSeparateReview"
    assert changed_preview["changedComponents"] == []
    assert_denied(changed_preview)
    print("PASS: real .NET choice, exact retry, stale/revision conflict and no automatic carry")

    bad = []
    def corrupt(mutate):
        x = copy.deepcopy(dict(previous=previous,current=previous,
                               history=[original],attempt=None))
        mutate(x)
        bad.append(x)
    corrupt(lambda x:x["history"][0].update(expectedRevision=1))
    corrupt(lambda x:x["history"][0].update(requestId=str(uuid.UUID(int=0))))
    corrupt(lambda x:x["history"][0].update(previewFingerprint="A"*64))
    corrupt(lambda x:x["history"][0].update(choice="1"))
    corrupt(lambda x:x["history"][0].update(accepted=True))
    corrupt(lambda x:x["previous"].update(memberRef="real:identity"))
    corrupt(lambda x:x["current"].update(memberRef="test:other-member"))
    corrupt(lambda x:x.update(officialChoiceRecorded=True))
    corrupt(lambda x:x.update(history=[original]*17))
    corrupt(lambda x:x["current"]["lines"][0].update(amount="0"))
    for entry in bad:
        status, result=send(entry)
        assert status == 400 and result["outcome"] == "INVALID_SYNTHETIC_CHOICE_INPUT", (status,entry,result)
        assert_denied(result)
    for kw in (
        dict(headers={"Origin":"https://attacker.example"}),
        dict(headers={"Origin":""}),
        dict(headers={"Host":"attacker.example:4318"}),
        dict(headers={"X-Forwarded-Host":"localhost"}),
        dict(raw=b'{"previous":{}, "previous":{}}'),
        dict(raw=b"{" + b"x"*17000 + b"}"),
    ):
        code, _ = send(base, **kw)
        assert code in (400,403), (kw,code)
    assert send(base,method="GET")[0] == 405
    print("PASS: test-only origin, strict schema, overflow, forged history and rights fail closed")


if __name__ == "__main__":
    run()
