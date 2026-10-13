#!/usr/bin/env python3
"""TRN-TECH-100: canonical 2-journal, revision-pinned PG support timeline."""
import json
from urllib.request import Request,urlopen
from urllib.error import HTTPError
from uuid import uuid4
from test_cost_journal_http import make_preview,call as cost_call,choice
from test_support_history_http import call as support_call,make as make_support
from test_cost_support_handoff_http import submit

ROOT="http://127.0.0.1:4320/internal/demo/v1/cost-previews"
def get(pid,case,after,limit,cost,support,extra="",headers=None):
    url=(f"{ROOT}/{pid}/support-link/{case}/timeline?afterRevision={after}"
         f"&limit={limit}&expectedCostChoiceRevision={cost}"
         f"&expectedSupportRevision={support}{extra}")
    try:
        with urlopen(Request(url,headers={"Accept":"application/json",**(headers or {})}),timeout=12) as response:
            return response.status,json.loads(response.read())
    except HTTPError as err:
        return err.code,json.loads(err.read())

def safe(x):
    assert x["contract"]=="TRN-TECH-100" and x["syntheticOnly"] is True
    assert x["atomicCrossJournalSnapshot"] is False
    for k in ("memberAuthenticated","insurerContacted","coverageAdjudicated",
              "complaintLegallySubmitted","officialChoiceRecorded","walletMutated",
              "financialCommitmentCreated","clinicalConsentGranted",
              "nationalReleaseAllowed"):
        assert x[k] is False,k

def main():
    preview=make_preview()
    pid,member=preview["previewId"],preview["memberRef"]
    registered=cost_call(body=preview)
    assert registered[0]==200
    fp=registered[1]["previewFingerprint"]
    case="test:cross-timeline-"+uuid4().hex
    evidence="test:cost-preview:"+pid

    missing=get(pid,case,0,3,0,1)
    assert missing[0]==404 and missing[1]["outcome"]=="TEST_SUPPORT_CASE_NOT_FOUND"
    safe(missing[1])
    initial=submit(case,member,evidence)

    # Five actual independently audited support events across two pages.
    for previous in range(1,5):
        command=make_support(member,evidence,initial["claimedSubmitterRef"],
                             previous,initial["requestId"])
        status,result=support_call(case+"/commands",command)
        assert status==200 and result["revision"]==previous+1,(status,result)
    first=get(pid,case,0,3,0,5)
    status,body=first
    assert status==200 and body["outcome"]=="VERIFIED_UNVERIFIED_INSURANCE_TIMELINE"
    assert body["previewFingerprint"]==fp
    assert body["originalSubmissionRequestId"]==initial["requestId"]
    assert body["independentlyReattested"] is True
    assert body["supportRevision"]==5 and body["costChoiceRevision"]==0
    assert body["hasMore"] and body["nextAfterRevision"]==3
    assert [v["revision"] for v in body["events"]]==[1,2,3]
    assert [v["action"] for v in body["events"]]==[
        "SUBMIT_UNVERIFIED","FOLLOW_UP_UNVERIFIED","FOLLOW_UP_UNVERIFIED"]
    assert all(v["hypotheticalOnly"] and not v["insurerVerified"] for v in body["events"])
    safe(body)
    status,second=get(pid,case,3,3,0,5)
    assert status==200 and [v["revision"] for v in second["events"]]==[4,5]
    assert not second["hasMore"] and second["nextAfterRevision"] is None
    safe(second)

    # Concurrent independent cost writer invalidates page 2 even without
    # modification to the support journal; revisions cannot be mixed.
    cost_event=choice(preview,fp,revision=0)
    assert cost_call("/"+pid+"/choices",cost_event)[1]["choiceRevision"]==1
    s,stale=get(pid,case,3,3,0,5)
    assert s==409 and stale["outcome"]=="CROSS_HISTORY_SOURCE_CHANGED"
    assert "events" not in stale and "previewFingerprint" not in stale
    safe(stale)
    assert get(pid,case,3,3,1,5)[0]==200

    # New support follow-up invalidates the caller's support-revision pin.
    next_command=make_support(member,evidence,initial["claimedSubmitterRef"],
                              5,initial["requestId"])
    assert support_call(case+"/commands",next_command)[1]["revision"]==6
    s,stale=get(pid,case,3,3,1,5)
    assert s==409 and stale["outcome"]=="CROSS_HISTORY_SOURCE_CHANGED"
    safe(stale)
    assert get(pid,case,3,3,1,6)[0]==200

    # Unrelated support cases, even with same syntactic evidence, cannot
    # masquerade as this member's insurance case.
    for other_member,ref,topic in (
        ("test:different-member",evidence,"INSURANCE_COVERAGE_DISPUTE"),
        (member,"test:unrelated-cost","INSURANCE_COVERAGE_DISPUTE"),
        (member,evidence,"MEDICAL_SERVICE_COMPLAINT")
    ):
        other="test:unrelated-"+uuid4().hex
        submit(other,other_member,ref,topic)
        s,x=get(pid,other,0,3,1,1)
        assert s==409 and x["outcome"]=="UNRELATED_TEST_SUPPORT_CASE"
        assert "events" not in x and "previewFingerprint" not in x
        safe(x)

    for bad in ("-1","01","1e2","9223372036854775807",""):
        for index in (0,2,3):
            args=["0","3","1","6"]
            args[index]=bad
            s,x=get(pid,case,*args)
            assert s==400 and x["outcome"]=="INVALID_TEST_CROSS_HISTORY_CURSOR",(args,s,x)
            safe(x)
    for cursor,lim in [(7,3),(0,0),(0,21)]:
        s,x=get(pid,case,cursor,lim,1,6)
        assert s==400 and x["outcome"]=="INVALID_TEST_CROSS_HISTORY_CURSOR"
    for suffix in ("&admin=true","&afterRevision=1","&expectedSupportRevision=1"):
        s,x=get(pid,case,0,3,1,6,extra=suffix)
        assert s==400 and x["outcome"]=="INVALID_TEST_CROSS_HISTORY_CURSOR"
    for invalid in (pid.upper(),"00000000-0000-0000-0000-000000000000","bad"):
        s,x=get(invalid,case,0,3,1,6)
        assert s==400 and x["outcome"]=="INVALID_TEST_CROSS_HISTORY_CURSOR"
    s,x=get(str(uuid4()),case,0,3,1,6)
    assert s==404 and x["outcome"]=="TEST_COST_PREVIEW_NOT_FOUND"
    safe(x)
    for headers in ({"Host":"hostile.invalid:4320"},{"X-Forwarded-Host":"hostile.invalid"}):
        s,x=get(pid,case,0,3,1,6,headers=headers)
        assert s==403 and x["nationalReleaseAllowed"] is False

    assert support_call(case)[1]["revision"]==6
    print("PASS: actual dual-PG journal timeline, 3+2 pages, stale cost/support, untrusted scope and deny-only")

if __name__=="__main__":
    main()
