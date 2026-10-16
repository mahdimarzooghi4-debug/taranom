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

    # Real, independent DB sessions. A browser cannot substitute its own
    # tenant or assumed role; effective SQL privileges are attested server-side.
    writer="taranom_ai_denial_writer_test"
    reader="taranom_ai_denial_reader_test"
    assert writer != reader
    def role_sql(role_name, password_key, sql, ok):
        import os
        secret=os.environ[password_key]
        assert len(secret)>=40
        command=["docker","exec","-e","PGPASSWORD="+secret,ids[0],
                 "psql","-X","-q","-t","-A","-v","ON_ERROR_STOP=1",
                 "-h","127.0.0.1","-U",role_name,
                 "-d","taranom_sprint64_center","-c",sql]
        test=subprocess.run(command,capture_output=True,text=True,timeout=12)
        assert (test.returncode==0)==ok, (role_name,ok,test.returncode)
        return test.stdout.strip()
    for actor,env_key in (
        (writer,"TARANOM_AI_AUDIT_WRITER_TEST_PASSWORD"),
        (reader,"TARANOM_AI_AUDIT_READER_TEST_PASSWORD")
    ):
        current=role_sql(actor,env_key,"SELECT current_user || ':' || session_user",True)
        assert current==actor+":"+actor
        role_sql(actor,env_key,
           "SELECT COUNT(*) FROM internal_test_ai_admin_denial",True)
        # Trigger rejects UPDATE/DELETE even if the role somehow had rights.
        for sql in (
          "UPDATE internal_test_ai_admin_denial SET event_sha=event_sha",
          "DELETE FROM internal_test_ai_admin_denial",
          "TRUNCATE internal_test_ai_admin_denial",
          "SELECT * FROM internal_test_support_case",
          "INSERT INTO internal_test_support_case(case_ref) VALUES ('test:forged')",
          "CREATE TABLE public.internal_test_attacker(id int)",
          "SET ROLE postgres"
        ):
            role_sql(actor,env_key,sql,False)
    role_sql(reader,"TARANOM_AI_AUDIT_READER_TEST_PASSWORD",
        "INSERT INTO internal_test_ai_admin_denial"
        "(seq,request_ref,operation,target_ref,evidence_status,previous_sha,event_sha)"
        " VALUES (999,'test:injected','ReadModelInventory','test:bad',"
        "'IDENTITY_NOT_VERIFIED_ACCESS_DENIED',repeat('0',64),repeat('0',64))",False)
    # The writer can INSERT only append-only *denial* rows. Application
    # journal integrity checks reject an unchained forged record at read.
    # No raw role passwords or DB account details are included in outputs.
    owner=subprocess.run(["docker","exec",ids[0],"psql","-X","-q","-t","-A",
        "-v","ON_ERROR_STOP=1","-U","postgres","-d","taranom_sprint64_center",
        "-c","SELECT COUNT(*) FROM pg_auth_members m JOIN pg_roles r "
        "ON m.member=r.oid WHERE r.rolname IN ('"+writer+"','"+reader+"');"],
        capture_output=True,text=True,timeout=12,check=True)
    assert owner.stdout.strip()=="0"
    print("PASS: 8/8 AI commands denied + audited in real Postgres; strict replay, "
          "linked audit SHA, concurrent writes, DB UPDATE prohibition, no privilege")

if __name__=="__main__":main()
