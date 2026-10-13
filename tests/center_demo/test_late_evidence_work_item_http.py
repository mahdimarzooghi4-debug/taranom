#!/usr/bin/env python3
"""TRN-TECH-114: real Kestrel/PostgreSQL synthetic-only late evidence work item."""
import json
from uuid import uuid4
from urllib.error import HTTPError
from urllib.request import Request,urlopen
BASE="http://127.0.0.1:4320/internal/demo/v1/late-evidence/"
def call(case,payload=None):
    body=None if payload is None else json.dumps(payload).encode()
    headers={"Accept":"application/json"}
    if body is not None:
        headers["Content-Type"]="application/json"
        headers["Origin"]="http://127.0.0.1:4320"
    req=Request(BASE+case+("/commands" if body else ""),data=body,headers=headers,
        method="POST" if body else "GET")
    try:
        with urlopen(req,timeout=8) as x:return x.status,json.loads(x.read())
    except HTTPError as e:return e.code,json.loads(e.read())
def command(rev,action,member,doc,actor,original):
    submit=action=="SUBMIT_UNVERIFIED_EVIDENCE"
    return {"requestId":str(uuid4()),"expectedRevision":rev,"action":action,
        "memberRef":member,"periodStartInclusive":"2026-01-01T00:00:00Z",
        "periodEndExclusive":"2026-02-01T00:00:00Z",
        "evidenceRef":doc,"submittedByClaimRef":actor,
        "originalSubmissionRequestId":original,
        "reviewerClaimRef":None if submit else "test:claimed-independent-reviewer",
        "reviewNoteEvidenceRef":None if submit else "test:unverified-review-note"}
def rights(x):
    assert x["contract"]=="TRN-TECH-114" and x["syntheticOnly"]
    assert x["untrustedClaimsOnly"]
    for k in ("realMemberAuthenticated","realReviewerAuthenticated",
       "officialMembershipVerified","legalEvidenceVerified",
       "evidenceSourceAuthenticated","entitlementApproved",
       "benefitAmountCalculated","walletCredited","financialLedgerMutated",
       "clinicalAccessGranted","nationalReleaseAllowed"):
        assert x[k] is False,k
def main():
    c="test:late-http-"+uuid4().hex
    member="test:member-"+uuid4().hex
    doc="test:evidence-"+uuid4().hex
    actor="test:submitter-"+uuid4().hex
    missing="00000000-0000-0000-0000-000000000000"
    assert call(c)[1]["revision"]==0
    original=command(0,"SUBMIT_UNVERIFIED_EVIDENCE",member,doc,actor,missing)
    status,submitted=call(c,original)
    assert status==200 and submitted["revision"]==1,(status,submitted)
    rights(submitted)
    note=command(1,"RECORD_UNVERIFIED_HUMAN_NOTE",member,doc,actor,original["requestId"])
    status,second=call(c,note)
    assert status==200 and second["revision"]==2,(status,second)
    assert [e["action"] for e in second["events"]]==[
        "SubmitUnverifiedEvidence","RecordUnverifiedHumanNote"]
    rights(second)
    assert call(c,original)[1]["outcome"]=="Replayed"
    assert call(c,{**original,"evidenceRef":"test:altered"})[1]["outcome"]=="ConflictingReplay"
    assert call(c,{**note,"requestId":str(uuid4()),"memberRef":"test:other"})[1]["outcome"]=="StaleRevision"
    assert call(c)[1]["revision"]==2
    assert call("test:late-other-"+uuid4().hex)[1]["revision"]==0
    assert call("real:member")[0]==400
    print("PASS: genuine PG late period claimed evidence and reviewer note, 2 events, exact replay, no finance authority")
if __name__=="__main__":main()
