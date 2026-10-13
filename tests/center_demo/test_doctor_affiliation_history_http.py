#!/usr/bin/env python3
"""TRN-TECH-103 actual Kestrel + PostgreSQL center doctor lineage, no licences."""
import json
from uuid import uuid4
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from test_real_center_demo_http import call,command

ROOT="http://127.0.0.1:4320/internal/demo/v1/centers/"

def history(center,doctor,current,at,after=0,limit=3,extra="",headers=None):
    uri=(ROOT+center+"/doctors/"+doctor+"/affiliation-history?expectedCurrentVersion="+
         str(current)+"&asOfVersion="+str(at)+"&afterVersion="+str(after)+
         "&limit="+str(limit)+extra)
    try:
        with urlopen(Request(uri,headers={"Accept":"application/json",**(headers or {})}),timeout=12) as res:
            return res.status,json.loads(res.read())
    except HTTPError as err:
        return err.code,json.loads(err.read())

def write(center,expected,kind,dep=None,doc=None):
    status,_,result=call(center+"/commands",command(expected,kind,dep,doc))
    assert status==200 and result["currentVersion"]==expected+1,(status,result)

def rights(response):
    assert response["contract"]=="TRN-TECH-103"
    assert response["syntheticOnly"] is True
    for key in ("managerAuthenticated","professionalQualificationVerified",
                "medicalLicenseVerified","clinicalAccessGranted",
                "providerContractVerified","billingAllowed","nationalReleaseAllowed"):
        assert response[key] is False,(key,response)

def main():
    tag=uuid4().hex
    center="test:doctorline-"+tag
    department_a="test:ward-a-"+tag
    department_b="test:ward-b-"+tag
    doctor="test:physician-"+tag
    second="test:other-physician-"+tag
    missing=history(center,doctor,0,0)
    assert missing[0]==404 and missing[1]["outcome"]=="TEST_DOCTOR_NOT_AFFILIATED_AT_VERSION"
    rights(missing[1])
    write(center,0,"DEFINE_DEPARTMENT",department_a)
    write(center,1,"DEFINE_DEPARTMENT",department_b)
    write(center,2,"RECORD_DOCTOR",doc=doctor)
    write(center,3,"ASSIGN_DOCTOR",department_a,doctor)
    write(center,4,"RECORD_DOCTOR",doc=second)
    write(center,5,"ASSIGN_DOCTOR",department_b,doctor)
    write(center,6,"ASSIGN_DOCTOR",department_a,second)
    write(center,7,"END_DOCTOR",doc=doctor)

    status,current=history(center,doctor,8,8)
    assert status==200 and current["outcome"]=="UNVERIFIED_DOCTOR_AFFILIATION_HISTORY"
    assert current["historyReplayed"] is True and not current["historicalOnly"]
    assert current["affiliation"]=="Ended" and current["departmentRefs"]==[]
    assert current["hasMore"] is True and current["nextAfterVersion"]==6
    assert [e["version"] for e in current["events"]]==[3,4,6]
    assert [e["kind"] for e in current["events"]]==[
        "RecordDoctorAffiliation","AssignDoctorToDepartment",
        "AssignDoctorToDepartment"]
    assert all(e["hypotheticalOnly"] and not e["licenseVerified"] and
               not e["clinicalPrivilegesGranted"] for e in current["events"])
    rights(current)
    status,ending=history(center,doctor,8,8,after=6)
    assert status==200 and [e["version"] for e in ending["events"]]==[8]
    assert ending["events"][0]["kind"]=="EndDoctorAffiliation"
    assert ending["events"][0]["departmentRef"] is None
    assert not ending["hasMore"] and ending["nextAfterVersion"] is None
    rights(ending)

    status,past=history(center,doctor,8,6)
    assert status==200 and past["historicalOnly"] is True
    assert past["affiliation"]=="PendingIndependentVerification"
    assert past["departmentRefs"]==sorted([department_a,department_b])
    assert [e["version"] for e in past["events"]]==[3,4,6]
    rights(past)
    status,earlier=history(center,doctor,8,3)
    assert status==200 and earlier["departmentRefs"]==[]
    assert [e["version"] for e in earlier["events"]]==[3]
    assert history(center,doctor,8,2)[0]==404
    assert history(center,second,8,6)[1]["departmentRefs"]==[]
    assert history("test:unrelated-"+tag,doctor,0,0)[0]==404

    # A legitimate new event from the SAME center invalidates every old read,
    # even though its target doctor is different.
    third="test:new-physician-"+tag
    write(center,8,"RECORD_DOCTOR",doc=third)
    status,stale=history(center,doctor,8,6)
    assert status==409 and stale["outcome"]=="TEST_DOCTOR_HISTORY_VERSION_CHANGED"
    assert "events" not in stale and "doctorRef" not in stale
    rights(stale)
    assert history(center,doctor,9,6)[0]==200
    assert history(center,doctor,9,10)[0]==400

    for bad in ("-1","00","1e0","9223372036854775808",""):
        for index in range(4):
            v=["9","6","0","3"];v[index]=bad
            code,out=history(center,doctor,*v)
            assert code==400 and out["outcome"]=="INVALID_TEST_DOCTOR_HISTORY_QUERY",(v,code,out)
            rights(out)
    for params in ("&role=clinical","&asOfVersion=5","&limit=2"):
        code,out=history(center,doctor,9,6,extra=params)
        assert code==400 and out["outcome"]=="INVALID_TEST_DOCTOR_HISTORY_QUERY"
        rights(out)
    for center_ref,doctor_ref in (("real:center",doctor),(center,"real:doctor")):
        code,out=history(center_ref,doctor_ref,9,6)
        assert code==400 and out["outcome"]=="INVALID_TEST_DOCTOR_HISTORY_QUERY"
        rights(out)
    code,out=history(center,doctor,9,6,after=7)
    assert code==400 and out["outcome"]=="INVALID_TEST_DOCTOR_HISTORY_QUERY"
    rights(out)
    for hostile in ({"Host":"wrong.example:4320"},{"X-Forwarded-Host":"wrong.example"}):
        code,out=history(center,doctor,9,6,headers=hostile)
        assert code==403 and out["nationalReleaseAllowed"] is False
    assert call(center)[2]["currentVersion"]==9
    print("PASS: 9 real PostgreSQL center transitions, doctor-specific sparse 3+1 pages, historical ending, independent tenant scope, stale source, all rights false")

if __name__=="__main__":
    main()
