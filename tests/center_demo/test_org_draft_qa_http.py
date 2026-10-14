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
    print("PASS: real ASP.NET organization QA via restricted PostgreSQL, exact replay, CAS and denied authority")

if __name__=="__main__":
    main()
