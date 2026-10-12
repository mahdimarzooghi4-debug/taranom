#!/usr/bin/env python3
"""TRN-TECH-097 real PostgreSQL ×2 + Kestrel independent reference checks."""
import json
from uuid import uuid4
from urllib.request import Request,urlopen
from urllib.error import HTTPError
from test_cost_journal_http import make_preview,call as cost_call,choice
from test_support_history_http import call as support_call,make as support_command

HOST="http://127.0.0.1:4320"
ZERO="00000000-0000-0000-0000-000000000000"

def inspect(preview,case,headers=None,suffix=""):
    url=HOST+"/internal/demo/v1/cost-previews/"+preview+"/support-link/"+case+suffix
    req=Request(url,headers={"Accept":"application/json",**(headers or {})})
    try:
        with urlopen(req,timeout=12) as response:
            return response.status,json.loads(response.read())
    except HTTPError as err:
        payload=err.read()
        return err.code,json.loads(payload) if payload else {}

def safe(d):
    assert d["contract"]=="TRN-TECH-097"
    assert d["syntheticOnly"] is True
    assert d["atomicCrossJournalSnapshot"] is False
    for k in ("memberAuthenticated","insurerContacted","coverageAdjudicated",
              "complaintLegallySubmitted","officialChoiceRecorded",
              "walletMutated","financialCommitmentCreated","clinicalConsentGranted",
              "nationalReleaseAllowed"):
        assert d[k] is False,(k,d)

def submit(case,member,evidence,topic="INSURANCE_COVERAGE_DISPUTE"):
    data=support_command(member,evidence,"test:operator",0,None)
    data["topic"]=topic
    code,value=support_call(case+"/commands",data)
    assert code==200 and value["revision"]==1,(code,value)
    return data

def main():
    p=make_preview()
    code,registered=cost_call(body=p)
    assert code==200 and registered["outcome"]=="Registered",(code,registered)
    id=p["previewId"]
    case="test:handoff-"+uuid4().hex
    evidence="test:cost-preview:"+id
    code,missing=inspect(id,case)
    assert code==404 and missing["outcome"]=="TEST_SUPPORT_CASE_NOT_FOUND"
    safe(missing)
    initial=submit(case,p["memberRef"],evidence)
    code,linked=inspect(id,case)
    assert code==200 and linked["outcome"]=="UNVERIFIED_CROSS_JOURNAL_REFERENCE"
    safe(linked)
    assert linked["independentlyReattested"] is True
    assert linked["memberRef"]==p["memberRef"] and linked["previewId"]==id
    assert linked["previewFingerprint"]==registered["previewFingerprint"]
    assert linked["costChoiceRevision"]==0 and linked["supportRevision"]==1
    assert linked["supportFollowUpCount"]==0
    assert linked["evidenceRef"]==evidence

    # Each actual journal evolves *independently*, and the cross-read must
    # freshly reflect both verified histories without claiming transactionality.
    status,recorded=cost_call("/"+id+"/choices",
                             choice(p,registered["previewFingerprint"]))
    assert status==200 and recorded["choiceRevision"]==1
    status,again=inspect(id,case)
    assert status==200 and again["costChoiceRevision"]==1
    follow=support_command(p["memberRef"],evidence,"test:operator",1,
                           initial["requestId"])
    status,support=support_call(case+"/commands",follow)
    assert status==200 and support["revision"]==2
    status,again=inspect(id,case)
    assert status==200 and again["costChoiceRevision"]==1
    assert again["supportRevision"]==2 and again["supportFollowUpCount"]==1
    safe(again)

    # Scope-mismatched, forged source, and medical complaint can NEVER be
    # passed off as insurance linkage.
    for member,src,topic in (
        ("test:other-member",evidence,"INSURANCE_COVERAGE_DISPUTE"),
        (p["memberRef"],"test:other-evidence","INSURANCE_COVERAGE_DISPUTE"),
        (p["memberRef"],evidence,"MEDICAL_SERVICE_COMPLAINT"),
    ):
        c="test:unrelated-"+uuid4().hex
        submit(c,member,src,topic)
        status,rejected=inspect(id,c)
        assert status==409 and rejected["outcome"]=="UNRELATED_TEST_SUPPORT_CASE"
        safe(rejected)
        assert rejected["independentlyReattested"] is False
        assert "memberRef" not in rejected and "previewFingerprint" not in rejected

    for bad in (id.upper(),"00000000-0000-0000-0000-000000000000","fake"):
        s,v=inspect(bad,case)
        assert s==400 and v["outcome"]=="INVALID_TEST_LINK_REF", (bad,s,v)
        safe(v)
    for bad in ("real:case","test:invalid!","test:"):
        s,v=inspect(id,bad)
        assert s in (400,404) and v["outcome"] in (
            "INVALID_TEST_LINK_REF","TEST_SUPPORT_CASE_NOT_FOUND"),(s,v)
        safe(v)
    s,v=inspect(str(uuid4()),case)
    assert s==404 and v["outcome"]=="TEST_COST_PREVIEW_NOT_FOUND"
    safe(v)
    for query in ("?memberRef=test:other","?admin=true","?a=1&a=2"):
        s,v=inspect(id,case,suffix=query)
        assert s==400 and v["outcome"]=="INVALID_TEST_LINK_REF"
    status,hostile=inspect(id,case,headers={"Host":"attacker.example:4320"})
    assert status==403 and hostile["nationalReleaseAllowed"] is False
    status,hostile=inspect(id,case,headers={"X-Forwarded-Host":"attacker"})
    assert status==403 and hostile["nationalReleaseAllowed"] is False
    print("PASS: two independent append-only PostgreSQL journals, exact scoped link, no financial/clinical authority")

if __name__=="__main__":
    main()
