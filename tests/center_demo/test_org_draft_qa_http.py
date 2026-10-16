#!/usr/bin/env python3
"""TRN-TECH-135: real localhost ASP.NET + restricted PostgreSQL QA."""
import json
from urllib.request import Request,urlopen
from urllib.error import HTTPError
from uuid import uuid4

HOST="http://127.0.0.1:4320"
BASE="/internal/demo/v1/org-drafts"

def send(path,body=None,headers=None):
    data=None if body is None else json.dumps(body).encode()
    h={"Accept":"application/json",**(headers or {})}
    if data is not None:h["Content-Type"]="application/json"
    req=Request(HOST+path,data=data,headers=h)
    try:
        with urlopen(req,timeout=15) as res:
            return res.status,json.loads(res.read())
    except HTTPError as exc:
        return exc.code,json.loads(exc.read())

def check(status,payload):
    assert payload["contract"]=="TRN-TECH-135",(status,payload)
    assert payload["source"]=="SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY"
    for name in ("organizationAuthenticated","operatorAuthorized",
                 "businessStatusChanged","nationalReleaseAllowed"):
        assert payload[name] is False,(name,payload)
    assert payload["syntheticOnly"] is True

def main():
    item="test:qah-"+uuid4().hex
    src="test:unverified-source"
    request="test:request-"+uuid4().hex
    status,created=send(BASE,{"itemRef":item,"sourceRef":src})
    check(status,created)
    assert status==200 and created["outcome"]=="Recorded",(status,created)
    status,replay=send(BASE,{"itemRef":item,"sourceRef":src})
    check(status,replay)
    assert status==200 and replay["outcome"]=="Replayed"
    status,conflict=send(BASE,{"itemRef":item,"sourceRef":"test:changed"})
    assert status==409 and conflict["outcome"]=="ConflictingReplay"
    status,found=send(BASE+"/"+item)
    check(status,found)
    assert status==200 and found["data"]=={"itemRef":item,"sourceRef":src}
    command={"requestRef":request,"noteRef":"test:manual-note","expectedPreviousNo":0}
    status,record=send(BASE+"/"+item+"/activity",command)
    check(status,record)
    assert status==200 and record["outcome"]=="Recorded"
    status,retry=send(BASE+"/"+item+"/activity",command)
    assert status==200 and retry["outcome"]=="Replayed"
    status,changed=send(BASE+"/"+item+"/activity",{**command,"noteRef":"test:different"})
    assert status==409 and changed["outcome"]=="ConflictingReplay"
    status,stale=send(BASE+"/"+item+"/activity",{
        "requestRef":"test:other-request","noteRef":"test:second","expectedPreviousNo":0})
    assert status==409 and stale["outcome"]=="StaleRevision"
    status,history=send(BASE+"/"+item+"/activity")
    check(status,history)
    assert status==200 and history["data"]["currentNo"]==1
    assert history["data"]["events"][0]["noteRef"]==command["noteRef"]
    status,listing=send(BASE+"?afterItemRef="+item[:-1]+"&limit=5")
    check(status,listing)
    assert status==200 and any(v["itemRef"]==item for v in listing["data"]["items"])
    for uri in (
        BASE+"?afterItemRef=&limit=0",
        BASE+"?afterItemRef=&limit=05",
        BASE+"?afterItemRef=&limit=5&scopeRef=test:scope-corporate",
        BASE+"/"+item+"?organization=test:scope-corporate"
    ):
        status,denied=send(uri)
        check(status,denied)
        assert status==400,(uri,status,denied)
    for header in (
        {"Authorization":"Bearer fake"},
        {"Cookie":"session=fake"},
        {"X-Org-Scope":"test:scope-corporate"}
    ):
        status,denied=send(BASE+"?afterItemRef=&limit=5",headers=header)
        check(status,denied)
        assert status==403,(header,status)
    for header in (
        {"Origin":"https://attacker.invalid"},
        {"X-Organization":"test:scope-corporate"}
    ):
        status,denied=send(BASE,{"itemRef":"test:denied","sourceRef":src},header)
        check(status,denied)
        assert status==403,(header,status)
    status,wrong=send(BASE,{"itemRef":"person:actual","sourceRef":src})
    check(status,wrong)
    assert status==400
    status,missing=send(BASE+"/test:missing-"+uuid4().hex)
    check(status,missing)
    assert status==404

    # TRN-TECH-136: new source+activity workqueue uses one read snapshot
    # and returns the actual latest activity, never a guessed status.
    status,queue=send("/internal/demo/v1/org-draft-queue?afterItemRef="+item[:-1]+"&limit=5")
    check(status,queue)
    assert status==200 and queue["outcome"]=="Read",(status,queue)
    rows=[row for row in queue["data"]["items"] if row["itemRef"]==item]
    assert len(rows)==1 and rows[0]["sourceRef"]==src
    assert rows[0]["activityCount"]==1
    assert rows[0]["lastRequestRef"]==request
    assert rows[0]["lastNoteRef"]==command["noteRef"]

    status,emptyQueue=send("/internal/demo/v1/org-draft-queue?afterItemRef="+item+"&limit=5")
    check(status,emptyQueue)
    assert status==200 and not any(
        x["itemRef"]==item for x in emptyQueue["data"]["items"])

    for uri in (
        "/internal/demo/v1/org-draft-queue",
        "/internal/demo/v1/org-draft-queue?afterItemRef=&limit=0",
        "/internal/demo/v1/org-draft-queue?afterItemRef=&limit=05",
        "/internal/demo/v1/org-draft-queue?afterItemRef=&limit=5&scopeRef=test:scope-corporate",
        "/internal/demo/v1/org-draft-queue?afterItemRef=person:real&limit=5",
    ):
        status,denied=send(uri)
        check(status,denied)
        assert status==400,(uri,status,denied)
    for header in (
        {"Authorization":"Bearer fake"},
        {"Cookie":"pretend=session"},
        {"X-Organization":"test:scope-corporate"},
    ):
        status,denied=send("/internal/demo/v1/org-draft-queue?afterItemRef=&limit=5",
                           headers=header)
        check(status,denied)
        assert status==403,(header,status,denied)


    # TRN-TECH-137: as-of and keyset timeline from the immutable real
    # PostgreSQL chain; each page pins exact current revision.
    for number in (1,2):
        status,added=send(BASE+"/"+item+"/activity",{
            "requestRef":"test:timelinereq-"+str(number),
            "noteRef":"test:timelinenote-"+str(number),
            "expectedPreviousNo":number})
        check(status,added)
        assert status==200 and added["outcome"]=="Recorded"
    prefix=BASE+"/"+item+"/activity-page?"
    def pg(after,limit,current,asof):
        return send(prefix+"afterNo="+str(after)+"&limit="+str(limit)+
                    "&expectedCurrentNo="+str(current)+"&asOfNo="+str(asof))
    status,one=pg(0,2,3,3)
    check(status,one)
    assert status==200 and one["outcome"]=="Read"
    assert one["data"]["currentNo"]==3 and one["data"]["asOfNo"]==3
    assert [x["activityNo"] for x in one["data"]["events"]]==[1,2]
    assert one["data"]["hasMore"] is True and one["data"]["nextAfterNo"]==2
    status,two=pg(2,2,3,3)
    check(status,two)
    assert status==200 and [x["activityNo"] for x in two["data"]["events"]]==[3]
    assert two["data"]["hasMore"] is False and two["data"]["nextAfterNo"] is None
    status,historic=pg(0,2,3,2)
    check(status,historic)
    assert status==200 and historic["data"]["hasMore"] is False
    assert len(historic["data"]["events"])==2
    status,zero=pg(0,2,3,0)
    check(status,zero)
    assert status==200 and zero["data"]["events"]==[]
    status,stale=pg(0,2,2,2)
    check(status,stale)
    assert status==409 and stale["outcome"]=="StaleRevision"
    assert stale["data"]["currentNo"]==3 and "events" not in stale["data"]
    for uri in (
        prefix+"afterNo=00&limit=2&expectedCurrentNo=3&asOfNo=3",
        prefix+"afterNo=0&limit=02&expectedCurrentNo=3&asOfNo=3",
        prefix+"afterNo=0&limit=2&expectedCurrentNo=3&asOfNo=4",
        prefix+"afterNo=4&limit=2&expectedCurrentNo=3&asOfNo=3",
        prefix+"afterNo=0&limit=6&expectedCurrentNo=3&asOfNo=3",
        prefix+"afterNo=0&limit=2&expectedCurrentNo=3",
        prefix+"afterNo=0&limit=2&expectedCurrentNo=3&asOfNo=3&scope=test:corporate",
    ):
        status,bad=send(uri)
        check(status,bad)
        assert status==400,(uri,status,bad)
    status,unauthorized=send(prefix+
        "afterNo=0&limit=2&expectedCurrentNo=3&asOfNo=3",
        headers={"Authorization":"Bearer untrusted"})
    check(status,unauthorized)
    assert status==403
    status,missing=send(BASE+"/test:timeline-missing-"+uuid4().hex+
        "/activity-page?afterNo=0&limit=2&expectedCurrentNo=0&asOfNo=0")
    check(status,missing)
    assert status==404

    print("PASS: real ASP.NET organization QA via restricted PostgreSQL, exact replay, CAS and denied authority")

if __name__=="__main__":
    main()
