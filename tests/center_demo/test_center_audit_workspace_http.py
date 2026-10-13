#!/usr/bin/env python3
"""TRN-TECH-104 genuine Kestrel/PostgreSQL combined same-snapshot audit."""
import json
from uuid import uuid4
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from test_real_center_demo_http import call, command

ROOT="http://127.0.0.1:4320/internal/demo/v1/centers/"

def read(center,current,at,after=0,limit=3,extra="",headers=None):
    path=(ROOT+center+"/audit-workspace?expectedCurrentVersion="+str(current)+
          "&asOfVersion="+str(at)+"&afterVersion="+str(after)+
          "&limit="+str(limit)+extra)
    try:
        with urlopen(Request(path,headers={"Accept":"application/json",**(headers or {})}),
                     timeout=12) as reply:
            return reply.status,json.loads(reply.read())
    except HTTPError as error:
        return error.code,json.loads(error.read())

def write(center,version,kind,department=None,doctor=None):
    status,_,data=call(center+"/commands",command(version,kind,department,doctor))
    assert status==200 and data["currentVersion"]==version+1,(status,data)

def denied_rights(data):
    assert data["contract"]=="TRN-TECH-104" and data["syntheticOnly"] is True
    assert data["durableCrossRequestSnapshot"] is False
    for name in ("managerAuthenticated","professionalQualificationVerified",
                 "clinicalAccessGranted","providerContractVerified",
                 "billingAllowed","nationalReleaseAllowed"):
        assert data[name] is False,(name,data)

def main():
    suffix=uuid4().hex
    center="test:audit-center-"+suffix
    dept1="test:ward-a-"+suffix
    dept2="test:ward-b-"+suffix
    doctor1="test:doc-a-"+suffix
    doctor2="test:doc-b-"+suffix

    code,empty=read(center,0,0)
    assert code==200 and empty["snapshot"]["eventCount"]==0
    assert empty["events"]==[] and not empty["hasMore"]
    denied_rights(empty)
    for version,kind,department,doctor in (
        (0,"DEFINE_DEPARTMENT",dept1,None),
        (1,"RECORD_DOCTOR",None,doctor1),
        (2,"ASSIGN_DOCTOR",dept1,doctor1),
        (3,"DEFINE_DEPARTMENT",dept2,None),
        (4,"ASSIGN_DOCTOR",dept2,doctor1),
        (5,"RECORD_DOCTOR",None,doctor2),
        (6,"END_DOCTOR",None,doctor1),
    ):
        write(center,version,kind,department,doctor)

    code,first=read(center,7,5)
    assert code==200 and first["outcome"]=="VERSION_PINNED_CENTER_AUDIT_WORKSPACE"
    assert first["sourceReplayed"] and first["sameSourceSnapshot"]
    assert first["currentVersion"]==7 and first["asOfVersion"]==5
    assert first["snapshot"]["currentVersion"]==5
    assert first["snapshot"]["eventCount"]==5
    assert sorted(first["snapshot"]["departments"])==sorted([dept1,dept2])
    assert first["snapshot"]["doctors"][0]["doctorRef"]==doctor1
    assert first["snapshot"]["doctors"][0]["affiliation"]=="PendingIndependentVerification"
    assert sorted(first["snapshot"]["doctors"][0]["departmentRefs"])==sorted([dept1,dept2])
    assert [e["version"] for e in first["events"]]==[1,2,3]
    assert first["hasMore"] and first["nextAfterVersion"]==3
    assert all(e["hypotheticalOnly"] and not e["professionalQualificationVerified"] and
               not e["clinicalPrivilegesGranted"] for e in first["events"])
    denied_rights(first)
    code,second=read(center,7,5,after=3)
    assert code==200 and [e["version"] for e in second["events"]]==[4,5]
    assert not second["hasMore"] and second["nextAfterVersion"] is None
    assert second["snapshot"]==first["snapshot"]
    denied_rights(second)

    code,ending=read(center,7,7)
    assert code==200 and ending["snapshot"]["eventCount"]==7
    doctor_map={x["doctorRef"]:x for x in ending["snapshot"]["doctors"]}
    assert doctor_map[doctor1]["affiliation"]=="Ended"
    assert doctor_map[doctor1]["departmentRefs"]==[]
    assert doctor_map[doctor2]["affiliation"]=="PendingIndependentVerification"
    assert ending["snapshot"]["departments"]==first["snapshot"]["departments"]
    for cursor,versions in ((0,[1,2,3]),(3,[4,5,6]),(6,[7])):
        status,result=read(center,7,7,after=cursor)
        assert status==200 and [v["version"] for v in result["events"]]==versions
        assert result["snapshot"]==ending["snapshot"]
        denied_rights(result)

    # A distinct center cannot provide the claimed first center history.
    other="test:separate-center-"+suffix
    code,different=read(other,0,0)
    assert code==200 and different["events"]==[]
    assert different["centerRef"]==other and different["snapshot"]["doctors"]==[]
    denied_rights(different)

    # Any legitimate write, even an unrelated doctor, invalidates a prior
    # center revision before delivering the page OR as-of state.
    write(center,7,"ASSIGN_DOCTOR",dept1,doctor2)
    code,stale=read(center,7,5,after=3)
    assert code==409 and stale["outcome"]=="TEST_CENTER_AUDIT_VERSION_CHANGED"
    assert "snapshot" not in stale and "events" not in stale
    denied_rights(stale)
    code,fresh=read(center,8,5,after=3)
    assert code==200 and fresh["snapshot"]==first["snapshot"]
    denied_rights(fresh)

    for bad in ("-1","01","2.0","1e1","9223372036854775808",""):
        for col in range(4):
            args=["8","5","0","3"]
            args[col]=bad
            code,out=read(center,*args)
            assert code==400 and out["outcome"]=="INVALID_TEST_CENTER_AUDIT_QUERY",(args,code,out)
            denied_rights(out)
    for args in ((8,9,0,3),(8,5,6,3),(8,5,0,0),(8,5,0,21)):
        code,out=read(center,*args)
        assert code==400
        denied_rights(out)
    for extra in ("&admin=true","&expectedCurrentVersion=7","&limit=1","&asOfVersion=3"):
        code,out=read(center,8,5,extra=extra)
        assert code==400 and out["outcome"]=="INVALID_TEST_CENTER_AUDIT_QUERY"
        denied_rights(out)
    code,out=read("real:center",8,5)
    assert code==400 and out["outcome"]=="INVALID_TEST_CENTER_AUDIT_QUERY"
    denied_rights(out)
    for hostile in ({"Host":"attacker.example:4320"},{"X-Forwarded-Host":"attacker.example"}):
        code,out=read(center,8,5,headers=hostile)
        assert code==403 and out["nationalReleaseAllowed"] is False
    assert call(center)[2]["currentVersion"]==8
    print("PASS: one reattested center PG snapshot for history+as-of, 7 event pages, stale/ref/host fail-closed, no clinical access")

if __name__=="__main__":
    main()
