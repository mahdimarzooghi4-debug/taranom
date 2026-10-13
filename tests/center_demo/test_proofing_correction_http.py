#!/usr/bin/env python3
"""Synthetic member proofing -> genuine loopback Kestrel -> PostgreSQL journal."""
import json
from uuid import uuid4
from urllib.request import Request, urlopen
from urllib.error import HTTPError

BASE="http://127.0.0.1:4320/internal/demo/v1/proofing/"
def api(url,payload=None):
    body=None if payload is None else json.dumps(payload).encode()
    headers={"Accept":"application/json"}
    if body is not None:
        headers.update({"Origin":"http://127.0.0.1:4320","Content-Type":"application/json"})
    req=Request(BASE+url,data=body,headers=headers,
        method="GET" if body is None else "POST")
    try:
        with urlopen(req,timeout=8) as resp:
            return resp.status,json.loads(resp.read())
    except HTTPError as e:
        return e.code,json.loads(e.read())

def cmd(case,identity,action,observation):
    correct=action=="APPLICANT_CORRECTED_IDENTITY"
    return {"requestId":str(uuid4()),"expectedCaseRevision":case,
        "expectedIdentityRevision":identity,"action":action,
        "observation":"NONE" if correct else observation,
        "providerAndEvidenceValidated":"MISSING" if correct else "CONFIRMED",
        "applicantLinkValidated":"MISSING" if correct else "CONFIRMED",
        "evidenceCurrent":"MISSING" if correct else "CONFIRMED",
        "officialActivationStatusKnown":"CONFIRMED",
        "officiallyActivated":False,"identityDataChangedSinceEvidence":False}
def rights(v):
    assert v["syntheticOnly"] is True and v["claimsNotVerified"] is True
    for key in ("realMemberAuthenticated","realIdentityVerified","nationalIdStored",
        "realIdpContacted","officialMembershipActivated","waitingPeriodStarted",
        "walletCredited","clinicalRecordAccessGranted","sponsorClinicalAccessGranted",
        "nationalReleaseAllowed"):
        assert v[key] is False,(key,v)

def main():
    ref="test:proof-http-"+uuid4().hex
    st,empty=api(ref);assert st==200 and empty["caseRevision"]==0
    rights(empty)
    first=cmd(0,0,"OBSERVE_PROOFING","DEFINITIVE_MISMATCH")
    st,a=api(ref+"/commands",first)
    assert st==200 and a["review"]=="CorrectApplicantInformationThenReverify",(st,a)
    rights(a)
    st,replayed=api(ref+"/commands",first)
    assert st==200 and replayed["outcome"]=="Replayed" and replayed["caseRevision"]==1
    st,conflict=api(ref+"/commands",{**first,"observation":"VERIFIED_MATCH"})
    assert st==409 and conflict["outcome"]=="ConflictingReplay"
    correction=cmd(1,0,"APPLICANT_CORRECTED_IDENTITY","NONE")
    st,b=api(ref+"/commands",correction)
    assert st==200 and b["caseRevision"]==2 and b["identityRevision"]==1
    assert b["review"]=="AwaitingAuthoritativeVerification"
    st,stale=api(ref+"/commands",cmd(2,0,"OBSERVE_PROOFING","VERIFIED_MATCH"))
    assert st==409 and stale["outcome"]=="StaleIdentityRevision"
    st,c=api(ref+"/commands",cmd(2,1,"OBSERVE_PROOFING","VERIFIED_MATCH"))
    assert st==200 and c["caseRevision"]==3 and c["identityRevision"]==1
    assert c["review"]=="ValidatedProofingEvidenceOnly"
    assert [(e["caseRevision"],e["identityRevision"]) for e in c["events"]]==[
        (1,0),(2,1),(3,1)]
    rights(c)
    other="test:proof-independent-"+uuid4().hex
    assert api(other)[1]["caseRevision"]==0
    assert api(ref)[1]["caseRevision"]==3
    assert api("real:person")[0]==400
    assert api(ref+"/commands",{**first,"nationalId":"real forbidden"})[0]==400
    print("PASS: actual PG identity correction/reverification with 3 audited events, replay, stale + no activation/PII")
if __name__=="__main__":
    main()
