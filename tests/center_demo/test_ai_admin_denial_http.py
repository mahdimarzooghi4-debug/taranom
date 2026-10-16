#!/usr/bin/env python3
"""Real localhost Kestrel -> .NET deny boundary -> PostgreSQL audit contract."""
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from uuid import uuid4

ROOT="http://127.0.0.1:4320/internal/demo/v1/ai-admin/denials"
OPS=("ReadModelInventory","RegisterModelVersion","RegisterTrainingRequest",
     "ReadEvaluation","RequestCommitteeReview","RecordCommitteeApproval",
     "AuthorizeProductionActivation","RollbackProduction")

def call(path="", body=None, headers=None):
    data=None if body is None else json.dumps(body).encode("utf-8")
    req=Request(ROOT+path,data=data,headers={
        "Accept":"application/json",
        **({"Content-Type":"application/json"} if data is not None else {}),
        **(headers or {})},method="POST" if data is not None else "GET")
    try:
        with urlopen(req,timeout=15) as res:
            return res.status,json.loads(res.read())
    except HTTPError as err:
        return err.code,json.loads(err.read())

def check(status,res):
    assert res["contract"]=="TRN-TECH-147",(status,res)
    for flag in ("adminAuthenticated","operatorAuthorized","committeeApproved",
                 "productionAllowed","trainingAllowed","realIdentityEstablished"):
        assert res[flag] is False,(flag,res)
    assert res["syntheticOnly"] is True

def body(op,ref=None):
    return {"requestRef":ref or "test:admin-"+uuid4().hex,
            "operation":op,"targetRef":"test:ai-model-v1"}

def main():
    entries=[]
    for operation in OPS:
        payload=body(operation)
        status,result=call(body=payload)
        check(status,result)
        assert status==403 and result["outcome"]=="DENIAL_RECORDED",(operation,status,result)
        assert result["data"]["disposition"]=="DENIED"
        assert result["data"]["identityEvidenceStatus"]=="IDENTITY_NOT_VERIFIED_ACCESS_DENIED"
        entries.append((payload,result["data"]))
    # This is a real PostgreSQL append-only chain with adjacent SHA links.
    for (_,previous),(_,current) in zip(entries,entries[1:]):
        assert current["seq"]==previous["seq"]+1
        assert current["previousSha256"]==previous["eventSha256"]
    first,original=entries[0]
    s,r=call(body=first)
    check(s,r)
    assert s==403 and r["outcome"]=="REPLAY_DENIED"
    assert r["data"]["eventSha256"]==original["eventSha256"]
    changed={**first,"targetRef":"test:other"}
    s,r=call(body=changed)
    check(s,r)
    assert s==409 and r["outcome"]=="CONFLICTING_DENIAL_REPLAY"
    s,r=call("/"+first["requestRef"])
    check(s,r)
    assert s==200 and r["data"]==original,(s,r)
    s,r=call("/test:unknown-"+uuid4().hex)
    check(s,r)
    assert s==404
    for op in ("UNRECOGNIZED","ApproveProduction","SelectModel", "0"):
        s,r=call(body=body(op))
        check(s,r)
        assert s==400,(op,s,r)
    for obj in ({"requestRef":"real:member","operation":OPS[0],"targetRef":"test:model"},
                {**body(OPS[0]),"adminApproved":True},
                {**body(OPS[0]),"targetRef":"person:actual"}):
        s,r=call(body=obj)
        check(s,r)
        assert s==400,(s,r)
    for hdr in ({"Cookie":"session=pretend"},
                {"X-Admin":"true"},{"Origin":"https://evil.invalid"},
                {"X-Role":"admin"},{"X-Tenant":"test:other"}):
        s,r=call(body=body(OPS[0]),headers=hdr)
        check(s,r)
        assert s==403 and r["outcome"]=="UNTRUSTED_IDENTITY_HINT",(hdr,s,r)
    s,r=call(body=body("AuthorizeProductionActivation"),
        headers={"Authorization":"Bearer not-a-valid-jwt"})
    check(s,r)
    assert s==403 and r["outcome"]=="DENIAL_RECORDED"
    assert "not-a-valid-jwt" not in json.dumps(r)
    # Race-safe global ordered append with advisory transaction locking.
    with ThreadPoolExecutor(max_workers=6) as pool:
        results=list(pool.map(lambda n:call(body=body("RegisterModelVersion",
                    "test:parallel-"+str(n)+"-"+uuid4().hex)),range(8)))
    assert all(s==403 and item["outcome"]=="DENIAL_RECORDED" for s,item in results)
    # Database constraint is real, not a mocked storage contract.
    ps=subprocess.run(["docker","ps","-q","--filter","ancestor=postgres:18.6",
                       "--filter","publish=5432"],capture_output=True,text=True,check=True)
    ids=ps.stdout.split()
    assert len(ids)==1
    denial=subprocess.run(["docker","exec",ids[0],"psql","-X","-q","-v",
        "ON_ERROR_STOP=1","-U","postgres","-d","taranom_sprint64_center",
        "-c","UPDATE internal_test_ai_admin_denial SET event_sha=event_sha WHERE seq=1;"],
        capture_output=True,text=True,timeout=12)
    assert denial.returncode!=0, "APPEND_ONLY_DB_UPDATE_UNEXPECTEDLY_SUCCEEDED"
    s,r=call("/"+first["requestRef"])
    check(s,r)
    assert s==200 and r["data"]==original
    print("PASS: 8/8 AI commands denied + audited in real Postgres; strict replay, "
          "linked audit SHA, concurrent writes, DB UPDATE prohibition, no privilege")

if __name__=="__main__":main()
