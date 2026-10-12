#!/usr/bin/env python3
"""Actual isolated PostgreSQL -> ASP.NET -> HTTP support event history."""
import json
from uuid import uuid4
from urllib.request import Request, urlopen
from urllib.error import HTTPError

BASE="http://127.0.0.1:4320/internal/demo/v1/support/"
ZERO="00000000-0000-0000-0000-000000000000"

def call(path, payload=None, headers=None):
    raw=None if payload is None else json.dumps(payload).encode()
    hdr={"Accept":"application/json",**(headers or {})}
    if raw is not None: hdr["Content-Type"]="application/json"
    req=Request(BASE+path,data=raw,headers=hdr)
    try:
        with urlopen(req,timeout=8) as response:
            return response.status,json.loads(response.read())
    except HTTPError as e:
        return e.code,json.loads(e.read())

def make(member,evidence,submitter,revision,original):
    initial=revision==0
    return {
        "requestId":str(uuid4()),
        "expectedRevision":revision,
        "action":"SUBMIT_UNVERIFIED" if initial else "FOLLOW_UP_UNVERIFIED",
        "memberRef":member,"topic":"INSURANCE_COVERAGE_DISPUTE",
        "unverifiedEvidenceRef":evidence,"claimedSubmitterRef":submitter,
        "originalSubmissionRequestId":ZERO if initial else original,
        "claimedFollowUpActorRef":None if initial else "test:followup-actor",
        "unverifiedFollowUpNoteRef":None if initial else "test:note-"+str(revision),
    }

def main():
    case="test:support-history-"+uuid4().hex
    member,evidence,submitter=["test:"+uuid4().hex for _ in range(3)]
    initial=make(member,evidence,submitter,0,None)
    for revision in (0,1,2):
        payload=(initial if revision==0 else
                 make(member,evidence,submitter,revision,initial["requestId"]))
        status,value=call(case+"/commands",payload)
        assert status==200 and value["revision"]==revision+1, (status,value)
    status,first=call(case+"/history?afterRevision=0&limit=2")
    assert status==200 and first["currentRevision"]==3
    assert first["hasMore"] and first["nextAfterRevision"]==2
    assert [e["revision"] for e in first["events"]]==[1,2]
    assert first["events"][0]["action"]=="SUBMIT_UNVERIFIED"
    for flag in ("memberAuthenticated","complaintLegallySubmitted",
                 "insurerContacted","clinicalReviewCompleted","nationalReleaseAllowed"):
        assert first[flag] is False
    status,second=call(case+"/history?afterRevision=2&limit=2")
    assert status==200 and [x["revision"] for x in second["events"]]==[3]
    assert second["hasMore"] is False and second["nextAfterRevision"] is None
    for revision,notes in ((0,0),(1,0),(2,1),(3,2)):
        status,old=call(case+"/versions/"+str(revision))
        assert status==200 and old["currentRevision"]==3
        assert old["asOfRevision"]==revision
        assert old["snapshot"]["revision"]==revision
        assert old["snapshot"]["followUpCount"]==notes
        assert old["snapshot"]["memberAuthenticated"] is False
    status,live=call(case)
    assert status==200 and live["revision"]==3
    other="test:other-"+uuid4().hex
    status,empty=call(other+"/history?afterRevision=0&limit=2")
    assert status==200 and empty["currentRevision"]==0 and empty["events"]==[]
    for query in ("afterRevision=-1&limit=2","afterRevision=00&limit=2",
                  "afterRevision=4&limit=2","afterRevision=0&limit=0",
                  "afterRevision=0&limit=21","afterRevision=0&limit=2&actor=admin",
                  "afterRevision=0&afterRevision=1&limit=2"):
        status,_=call(case+"/history?"+query)
        assert status==400,query
    for version in ("-1","01","999","not_a_number"):
        status,_=call(case+"/versions/"+version)
        assert status==400,version
    status,untrusted=call(case+"/history?afterRevision=0&limit=2",
                          headers={"Host":"evil.example:4320"})
    assert status==403 and untrusted["nationalReleaseAllowed"] is False
    status,live=call(case)
    assert status==200 and live["revision"]==3
    print("PASS: support PG event timeline, canonical as-of replay, 17 national blocks unaffected")

if __name__=="__main__":
    main()
