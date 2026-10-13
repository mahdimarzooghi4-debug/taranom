#!/usr/bin/env python3
"""TRN-TECH-102: genuine .NET Kestrel -> existing PG center event+audit roster."""
import json
from uuid import uuid4
from urllib.error import HTTPError
from urllib.request import Request,urlopen
from test_real_center_demo_http import call,command

BASE="http://127.0.0.1:4320/internal/demo/v1/centers/"
def roster(center,department,current,at,extra="",headers=None):
    url=(BASE+center+"/departments/"+department+
        "/roster?expectedCurrentVersion="+str(current)+"&asOfVersion="+str(at)+extra)
    try:
        with urlopen(Request(url,headers={"Accept":"application/json",**(headers or {})}),timeout=12) as r:
            return r.status,json.loads(r.read())
    except HTTPError as e:
        return e.code,json.loads(e.read())

def denied_flags(v):
    assert v["contract"]=="TRN-TECH-102"
    assert v["syntheticOnly"] is True
    for k in ("managerAuthenticated","professionalQualificationVerified",
              "clinicalAccessGranted","providerContractVerified",
              "billingAllowed","nationalReleaseAllowed"):
        assert v[k] is False,(k,v)

def write(center,expected,kind,department=None,doctor=None):
    status,_,value=call(center+"/commands",command(expected,kind,department,doctor))
    assert status==200 and value["currentVersion"]==expected+1,(status,value)

def main():
    ident=uuid4().hex
    center="test:roster-"+ident
    dep_a="test:dep-a-"+ident
    dep_b="test:dep-b-"+ident
    d1="test:doctor-one-"+ident
    d2="test:doctor-two-"+ident
    assert roster(center,dep_a,0,0)[0]==404
    write(center,0,"DEFINE_DEPARTMENT",dep_a)
    write(center,1,"DEFINE_DEPARTMENT",dep_b)
    write(center,2,"RECORD_DOCTOR",doctor=d1)
    write(center,3,"ASSIGN_DOCTOR",dep_a,d1)
    write(center,4,"ASSIGN_DOCTOR",dep_b,d1)
    write(center,5,"RECORD_DOCTOR",doctor=d2)
    write(center,6,"ASSIGN_DOCTOR",dep_a,d2)
    status,prev=roster(center,dep_a,7,7)
    assert status==200 and prev["currentVersion"]==7 and prev["asOfVersion"]==7
    assert prev["outcome"]=="UNVERIFIED_DEPARTMENT_ROSTER"
    assert prev["historyReplayed"] is True and prev["historicalOnly"] is False
    assert [x["doctorRef"] for x in prev["doctors"]]==sorted([d1,d2])
    assert prev["doctorCount"]==2
    for x in prev["doctors"]:
        assert x["affiliation"]=="PendingIndependentVerification"
        assert x["assignmentUnverified"] is True
        assert x["clinicalPrivilegesGranted"] is False
        assert x["licensureVerified"] is False
    denied_flags(prev)
    assert [x["doctorRef"] for x in roster(center,dep_b,7,7)[1]["doctors"]]==[d1]
    assert roster(center,dep_a,7,3)[1]["doctors"]==[]
    assert roster(center,dep_a,7,4)[1]["doctorCount"]==1
    assert roster(center,dep_b,7,4)[1]["doctors"]==[]
    assert roster(center,dep_a,7,0)[0]==404
    write(center,7,"END_DOCTOR",doctor=d1)

    # Ended affiliation has no current department assignments but historical
    # version 7 must remain rehydratable after independent audited write.
    status,now=roster(center,dep_a,8,8)
    assert status==200 and [x["doctorRef"] for x in now["doctors"]]==[d2]
    denied_flags(now)
    assert roster(center,dep_b,8,8)[1]["doctorCount"]==0
    code,old=roster(center,dep_a,8,7)
    assert code==200 and old["historicalOnly"] is True
    assert [x["doctorRef"] for x in old["doctors"]]==sorted([d1,d2])
    denied_flags(old)
    code,stale=roster(center,dep_a,7,7)
    assert code==409 and stale["outcome"]=="TEST_CENTER_ROSTER_VERSION_CHANGED"
    assert "doctors" not in stale and "centerRef" not in stale
    denied_flags(stale)

    # Two centers can happen to use the SAME department and doctor test refs.
    # A cannot observe B unless those affiliations exist in A's own journal.
    other="test:other-center-"+ident
    write(other,0,"DEFINE_DEPARTMENT",dep_a)
    code,isolated=roster(other,dep_a,1,1)
    assert code==200 and isolated["doctorCount"]==0
    denied_flags(isolated)
    for cen,dep in (("real:center",dep_a),(center,"real:department")):
        code,out=roster(cen,dep,8,8)
        assert code==400 and out["outcome"]=="INVALID_TEST_ROSTER_QUERY"
        denied_flags(out)
    for v in ("-1","00","8.0","1e1","9223372036854775808",""):
        code,out=roster(center,dep_a,v,8)
        assert code==400 and out["outcome"]=="INVALID_TEST_ROSTER_QUERY",(v,code,out)
    for suffix in ("&admin=true","&expectedCurrentVersion=9","&asOfVersion=5"):
        code,out=roster(center,dep_a,8,8,suffix)
        assert code==400 and out["outcome"]=="INVALID_TEST_ROSTER_QUERY"
    assert roster(center,dep_a,8,9)[0]==400
    for headers in ({"Host":"attacker.example:4320"},{"X-Forwarded-Host":"attacker.example"}):
        code,out=roster(center,dep_a,8,8,headers=headers)
        assert code==403 and out["nationalReleaseAllowed"] is False
    assert call(center)[2]["currentVersion"]==8
    print("PASS: 8 real PG center writes, multi-department doctor roster, historical end, strict scope/version, no clinical rights")

if __name__=="__main__":
    main()
