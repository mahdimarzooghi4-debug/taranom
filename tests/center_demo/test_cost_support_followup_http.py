#!/usr/bin/env python3
"""TRN-TECH-099: real two-journal PG follow-up preflight and exact idempotency."""
from concurrent.futures import ThreadPoolExecutor
import json
from uuid import uuid4
from urllib.error import HTTPError
from urllib.request import Request,urlopen
from test_cost_journal_http import make_preview,call as cost_call,choice
from test_support_history_http import call as support_call,make as support_command
from test_cost_support_handoff_http import inspect,submit

HOST="http://127.0.0.1:4320/internal/demo/v1/cost-previews"

def preflight(id,case,cost,support,extra="",headers=None):
    url=f"{HOST}/{id}/support-link/{case}/follow-up-preflight?expectedCostChoiceRevision={cost}&expectedSupportRevision={support}"+extra
    req=Request(url,headers={"Accept":"application/json",**(headers or {})})
    try:
        with urlopen(req,timeout=12) as response:
            return response.status,json.loads(response.read())
    except HTTPError as error:
        return error.code,json.loads(error.read())

def deny_flags(out):
    assert out["contract"]=="TRN-TECH-099" and out["syntheticOnly"] is True
    assert out["atomicCrossJournalSnapshot"] is False
    for k in ("memberAuthenticated","insurerContacted","coverageAdjudicated",
              "complaintLegallySubmitted","officialChoiceRecorded","walletMutated",
              "financialCommitmentCreated","clinicalConsentGranted","nationalReleaseAllowed"):
        assert out[k] is False,(k,out)

def main():
    preview=make_preview()
    p=preview["previewId"]; member=preview["memberRef"]
    case="test:followup-"+uuid4().hex
    evidence="test:cost-preview:"+p
    status,registered=cost_call(body=preview)
    assert status==200,(status,registered)
    missing=preflight(p,case,0,1)
    assert missing[0]==404 and missing[1]["outcome"]=="TEST_SUPPORT_CASE_NOT_FOUND"
    deny_flags(missing[1])
    initial=submit(case,member,evidence)
    code,confirmed=preflight(p,case,0,1)
    assert code==200 and confirmed["outcome"]=="UNVERIFIED_FOLLOWUP_PREFLIGHT"
    assert confirmed["originalSubmissionRequestId"]==initial["requestId"]
    assert confirmed["claimedSubmitterRef"]==initial["claimedSubmitterRef"]
    assert confirmed["memberRef"]==member
    assert confirmed["independentlyReattested"] is True
    assert confirmed["costChoiceRevision"]==0 and confirmed["supportRevision"]==1
    deny_flags(confirmed)
    cmd=support_command(member,evidence,initial["claimedSubmitterRef"],1,initial["requestId"])
    status,recorded=support_call(case+"/commands",cmd)
    assert status==200 and recorded["revision"]==2
    assert recorded["followUpCount"]==1 and recorded["outcome"]=="RecordedUnverified"
    replay=support_call(case+"/commands",cmd)
    assert replay[0]==200 and replay[1]["revision"]==2
    assert replay[1]["outcome"]=="Replayed" and replay[1]["followUpCount"]==1
    status,stale=preflight(p,case,0,1)
    assert status==409 and stale["outcome"]=="TEST_FOLLOWUP_SOURCE_CHANGED"
    deny_flags(stale)
    assert "originalSubmissionRequestId" not in stale
    assert preflight(p,case,0,2)[0]==200

    # Source evolution independently revokes a pinned preflight.
    status,choice_saved=cost_call("/"+p+"/choices",
                                  choice(preview,registered["previewFingerprint"]))
    assert status==200 and choice_saved["choiceRevision"]==1
    assert preflight(p,case,0,2)[1]["outcome"]=="TEST_FOLLOWUP_SOURCE_CHANGED"
    assert preflight(p,case,1,2)[0]==200

    # Two same-revision follow-ups under the real existing PostgreSQL row lock:
    # one wins; other definitively stale. No double journal mutation.
    cmds=[support_command(member,evidence,initial["claimedSubmitterRef"],2,
                          initial["requestId"]) for _ in range(2)]
    with ThreadPoolExecutor(max_workers=2) as pool:
        writes=list(pool.map(lambda command:support_call(case+"/commands",command),cmds))
    assert sorted((status,value["outcome"]) for status,value in writes)==[
        (200,"RecordedUnverified"),(409,"StaleRevision")],writes
    assert support_call(case)[1]["revision"]==3
    assert preflight(p,case,1,2)[1]["outcome"]=="TEST_FOLLOWUP_SOURCE_CHANGED"
    assert preflight(p,case,1,3)[0]==200

    # Source owner, case scope, insurer vs medical route are independent.
    for other_member,other_evidence,topic in (
        ("test:other-member",evidence,"INSURANCE_COVERAGE_DISPUTE"),
        (member,"test:other-document","INSURANCE_COVERAGE_DISPUTE"),
        (member,evidence,"MEDICAL_SERVICE_COMPLAINT")
    ):
        other="test:unrelated-"+uuid4().hex
        submit(other,other_member,other_evidence,topic)
        code,result=preflight(p,other,1,1)
        assert code==409 and result["outcome"]=="UNRELATED_TEST_SUPPORT_CASE"
        assert "memberRef" not in result and "originalSubmissionRequestId" not in result
        deny_flags(result)

    for invalid in ("-1","01","1e0","9223372036854775807",""):
        code,result=preflight(p,case,invalid,3)
        assert code==400 and result["outcome"]=="INVALID_TEST_FOLLOWUP_PREFLIGHT",(invalid,code,result)
        deny_flags(result)
    for extra in ("&actor=admin","&expectedSupportRevision=2","&x=1&x=2"):
        code,result=preflight(p,case,1,3,extra=extra)
        assert code==400 and result["outcome"]=="INVALID_TEST_FOLLOWUP_PREFLIGHT"
    for invalid in (p.upper(),"00000000-0000-0000-0000-000000000000","bogus"):
        code,result=preflight(invalid,case,1,3)
        assert code==400 and result["outcome"]=="INVALID_TEST_FOLLOWUP_PREFLIGHT"
    for headers in ({"Host":"wrong.example:4320"},{"X-Forwarded-Host":"wrong.example"}):
        code,result=preflight(p,case,1,3,headers=headers)
        assert code==403 and result["nationalReleaseAllowed"] is False
    assert support_call(case)[1]["revision"]==3
    print("PASS: real PostgreSQL support follow-up preflight, stale sources, replay, concurrency and deny-only")

if __name__=="__main__":
    main()
