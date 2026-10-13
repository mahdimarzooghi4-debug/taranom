#!/usr/bin/env python3
"""TRN-TECH-098: actual canonical PostgreSQL cost audit, frozen cursor and as-of."""
import json
from urllib.request import urlopen,Request
from urllib.error import HTTPError
from uuid import uuid4
from test_cost_journal_http import make_preview,call,choice

ROOT="http://127.0.0.1:4320/internal/demo/v1/cost-previews"

def get(path,headers=None):
    try:
        with urlopen(Request(ROOT+path,headers={"Accept":"application/json",
                                                **(headers or {})}),timeout=10) as r:
            return r.status,json.loads(r.read())
    except HTTPError as err:
        raw=err.read()
        return err.code,json.loads(raw) if raw else {}

def page(pid,cursor,limit,expected,addition=""):
    return get(f"/{pid}/history?afterRevision={cursor}&limit={limit}"
               f"&expectedCurrentRevision={expected}"+addition)

def at(pid,revision,expected,addition=""):
    return get(f"/{pid}/versions/{revision}?expectedCurrentRevision={expected}"+addition)

def safe(v):
    assert v["contract"]=="TRN-TECH-098" and v["syntheticOnly"] is True
    for k in ("memberAuthenticated","officialChoiceRecorded",
              "choiceSignatureValid","insurerEntitlementVerified",
              "walletMutated","financialCommitmentCreated",
              "clinicalConsentGranted","nationalReleaseAllowed"):
        assert v[k] is False,(k,v)

def main():
    p=make_preview()
    pid=p["previewId"]
    status,registered=call(body=p)
    assert status==200
    fp=registered["previewFingerprint"]
    status,empty=page(pid,0,5,0)
    assert status==200 and empty["events"]==[] and not empty["hasMore"]
    assert empty["nextAfterRevision"] is None
    safe(empty)
    status,zero=at(pid,0,0)
    assert status==200 and zero["choiceCount"]==0
    assert zero["lastUnverifiedChoice"] is None and zero["autoCarriedForward"] is False
    safe(zero)

    choices=["AcknowledgedUncertainty","Deferred","RequestedClarification",
             "Deferred","AcknowledgedUncertainty","RequestedClarification"]
    for rev,which in enumerate(choices):
        cmd=choice(p,fp,revision=rev)
        cmd["choice"]=which
        status,result=call("/"+pid+"/choices",cmd)
        assert status==200 and result["choiceRevision"]==rev+1,(status,result)
    status,first=page(pid,0,5,6)
    assert status==200 and first["currentRevision"]==6
    assert first["expectedCurrentRevision"]==6
    assert first["hasMore"] and first["nextAfterRevision"]==5
    assert [e["revision"] for e in first["events"]]==[1,2,3,4,5]
    assert [e["choice"] for e in first["events"]]==choices[:5]
    assert all(e["hypotheticalOnly"] and not e["legallyBinding"] for e in first["events"])
    safe(first)
    status,last=page(pid,5,5,6)
    assert status==200 and not last["hasMore"] and last["nextAfterRevision"] is None
    assert [e["revision"] for e in last["events"]]==[6]
    assert last["events"][0]["choice"]==choices[5]
    safe(last)

    for revision in (0,1,3,6):
        status,historic=at(pid,revision,6)
        assert status==200 and historic["asOfRevision"]==revision
        assert historic["currentRevision"]==6
        assert historic["choiceCount"]==revision
        assert historic["lastUnverifiedChoice"]==(choices[revision-1] if revision else None)
        assert historic["historicalOnly"] and not historic["autoCarriedForward"]
        safe(historic)

    # A second legitimate writer advances the source AFTER page 1.
    # Page 2 cannot silently be joined to the previously pinned cursor.
    cmd=choice(p,fp,revision=6)
    assert call("/"+pid+"/choices",cmd)[1]["choiceRevision"]==7
    for invoke in (lambda:page(pid,5,5,6),lambda:at(pid,3,6)):
        status,denied=invoke()
        assert status==409 and denied["outcome"]=="TEST_COST_HISTORY_REVISION_CHANGED"
        safe(denied)
        assert "events" not in denied and "previewFingerprint" not in denied
    assert page(pid,5,5,7)[1]["events"][0]["revision"]==6
    assert at(pid,3,7)[1]["lastUnverifiedChoice"]==choices[2]

    for query in ("afterRevision=-1&limit=5&expectedCurrentRevision=7",
                  "afterRevision=00&limit=5&expectedCurrentRevision=7",
                  "afterRevision=8&limit=5&expectedCurrentRevision=7",
                  "afterRevision=0&limit=0&expectedCurrentRevision=7",
                  "afterRevision=0&limit=21&expectedCurrentRevision=7",
                  "afterRevision=0&limit=5",
                  "afterRevision=0&limit=5&expectedCurrentRevision=7&admin=true",
                  "afterRevision=0&limit=5&expectedCurrentRevision=7&limit=1",
                  "afterRevision=0&limit=5&expectedCurrentRevision=1e1"):
        s,out=get(f"/{pid}/history?"+query)
        assert s==400 and out["outcome"]=="INVALID_TEST_COST_HISTORY_CURSOR",(query,s,out)
        safe(out)
    for rev in ("-1","01","999","not-an-int"):
        s,out=at(pid,rev,7)
        assert s==400 and out["outcome"]=="INVALID_TEST_COST_HISTORY_REVISION"
        safe(out)
    s,out=at(pid,0,7,"&member=test:other")
    assert s==400 and out["outcome"]=="INVALID_TEST_COST_HISTORY_REVISION"
    safe(out)
    nonexistent=str(uuid4())
    for invoke in (lambda:page(nonexistent,0,5,0),lambda:at(nonexistent,0,0)):
        s,out=invoke()
        assert s==404 and out["outcome"]=="TEST_COST_PREVIEW_NOT_FOUND"
        safe(out)
    for invoke in (lambda:page(pid.upper(),0,5,7),lambda:at(pid.upper(),1,7)):
        s,out=invoke()
        assert s==400 and out["outcome"]=="INVALID_TEST_PREVIEW_REF" or (
            s==400 and out["outcome"]=="INVALID_TEST_COST_HISTORY_REVISION")
        safe(out)
    s,out=get(f"/{pid}/history?afterRevision=0&limit=5&expectedCurrentRevision=7",
              headers={"Host":"attacker.invalid:4320"})
    assert s==403 and out["nationalReleaseAllowed"] is False
    s,out=get(f"/{pid}/versions/0?expectedCurrentRevision=7",
              headers={"X-Forwarded-Host":"attacker.invalid"})
    assert s==403 and out["nationalReleaseAllowed"] is False
    assert call("/"+pid)[1]["choiceRevision"]==7
    print("PASS: real 7-choice PostgreSQL history, consistent frozen paging, as-of, denial and no authority")

if __name__=="__main__":
    main()
