#!/usr/bin/env python3
"""TRN-TECH-115 actual Kestrel/PG care coordination bounded safety replay."""
import json
from concurrent.futures import ThreadPoolExecutor
from uuid import uuid4
from urllib.request import urlopen, Request
from urllib.error import HTTPError
BASE="http://127.0.0.1:4320/internal/demo/v1/care-coordination/"
ZERO="00000000-0000-0000-0000-000000000000"
def api(ref,data=None):
    payload=None if data is None else json.dumps(data).encode()
    headers={"Accept":"application/json"}
    if payload is not None:
        headers.update({"Content-Type":"application/json","Origin":"http://127.0.0.1:4320"})
    req=Request(BASE+ref+("/commands" if payload is not None else ""),data=payload,
                method="POST" if payload is not None else "GET",headers=headers)
    try:
        with urlopen(req,timeout=9) as s:return s.status,json.loads(s.read())
    except HTTPError as e:return e.code,json.loads(e.read())
def initial():
    return dict(requestId=str(uuid4()),expectedRevision=0,
        action="REQUEST_NONCLINICAL_COORDINATION",
        memberRef="test:member-"+uuid4().hex,
        originalMemberRequestRef="test:reason-"+uuid4().hex,
        originalSubmissionRequestId=ZERO,claimedActorRef=None,
        unverifiedNoteOrDocumentRef=None)
def note(origin,revision,action):
    return dict(origin,requestId=str(uuid4()),expectedRevision=revision,
        action=action,originalSubmissionRequestId=origin["requestId"],
        claimedActorRef="test:staff-"+uuid4().hex,
        unverifiedNoteOrDocumentRef="test:document-"+uuid4().hex)
def main():
    c="test:care-http-"+uuid4().hex
    first=initial()
    result=api(c,first)
    assert result[0]==200 and result[1]["revision"]==1,result
    with ThreadPoolExecutor(max_workers=2) as pool:
        tasks=[pool.submit(api,c,note(first,1,"RECORD_UNVERIFIED_COORDINATOR_CONTACT"))
               for _ in range(2)]
        results=[t.result() for t in tasks]
    assert sorted(x[0] for x in results)==[200,409],results
    third=note(first,2,"ATTACH_UNVERIFIED_CLINICAL_SOURCE")
    assert api(c,third)[0]==200
    assert api(c,first)[1]["outcome"]=="Replayed"
    assert api(c,{**third,"claimedActorRef":"test:tamper"})[1]["outcome"]=="ConflictingReplay"
    status,read=api(c)
    assert status==200 and read["revision"]==3,(status,read)
    assert len(read["events"])==3
    for k in ("realMemberAuthenticated","realCoordinatorVerified","clinicalSourceVerified",
       "clinicianAuthorized","referralOrdered","referralDelivered",
       "appointmentBooked","treatmentStarted","medicalRecordAccessGranted",
       "emergencyServiceDispatched","consentGranted","walletMutated",
       "nationalReleaseAllowed"):
        assert read[k] is False,k
    assert api("test:care-independent-"+uuid4().hex)[1]["revision"]==0
    assert api("real:person")[0]==400

    # TRN-TECH-195: same authoritative PG replay, revision-pinned pages.
    history_path=BASE+c+"/history"
    def history(params):
        try:
            with urlopen(history_path+"?"+params,timeout=9) as resp:
                return resp.status,json.loads(resp.read())
        except HTTPError as exc:
            return exc.code,json.loads(exc.read())
    status,first_page=history("afterRevision=0&limit=2&expectedCurrentRevision=3")
    assert status==200 and first_page["contract"]=="TRN-TECH-195"
    assert first_page["currentRevision"]==3 and first_page["hasMore"]
    assert first_page["nextAfterRevision"]==2
    assert [e["revision"] for e in first_page["events"]]==[1,2]
    assert first_page["events"][0]["requestId"]==first["requestId"]
    assert first_page["events"][0]["originalSubmissionRequestId"]==ZERO
    assert first_page["events"][1]["originalSubmissionRequestId"]==first["requestId"]
    for key in ("realMemberAuthenticated","realCoordinatorVerified",
      "clinicalSourceVerified","clinicianAuthorized","referralOrdered",
      "referralDelivered","appointmentBooked","treatmentStarted",
      "medicalRecordAccessGranted","emergencyServiceDispatched",
      "consentGranted","walletMutated","nationalReleaseAllowed"):
        assert first_page[key] is False,key
    status,end=history("afterRevision=2&limit=2&expectedCurrentRevision=3")
    assert status==200 and end["events"][0]["revision"]==3
    assert not end["hasMore"] and end["nextAfterRevision"] is None
    status,empty=history("afterRevision=3&limit=2&expectedCurrentRevision=3")
    assert status==200 and empty["events"]==[] and not empty["hasMore"]
    for bad in ("afterRevision=00&limit=2&expectedCurrentRevision=3",
      "afterRevision=-1&limit=2&expectedCurrentRevision=3",
      "afterRevision=4&limit=2&expectedCurrentRevision=3",
      "afterRevision=0&limit=0&expectedCurrentRevision=3",
      "afterRevision=0&limit=11&expectedCurrentRevision=3",
      "afterRevision=0&limit=2&expectedCurrentRevision=3&source=medical",
      "afterRevision=0&limit=2&expectedCurrentRevision=3&limit=2",
      "afterRevision=0&limit=2",
      "afterRevision=0&limit=2&expectedCurrentRevision=03"):
        status,error=history(bad)
        assert status==400,(bad,status,error)
        assert error["nationalReleaseAllowed"] is False
    # TRN-TECH-197: sparse type-specific pages preserve ORIGINAL revisions.
    contact=history("afterRevision=0&limit=1&expectedCurrentRevision=3&claimType=CONTACT")
    assert contact[0]==200 and contact[1]["claimType"]=="CONTACT",contact
    assert [e["revision"] for e in contact[1]["events"]]==[2]
    assert contact[1]["nextAfterRevision"] is None
    source=history("afterRevision=0&limit=1&expectedCurrentRevision=3&claimType=SOURCE")
    assert source[0]==200 and source[1]["claimType"]=="SOURCE",source
    assert [e["revision"] for e in source[1]["events"]]==[3]
    assert source[1]["nextAfterRevision"] is None
    empty=history("afterRevision=2&limit=2&expectedCurrentRevision=3&claimType=CONTACT")
    assert empty[0]==200 and empty[1]["events"]==[],empty
    assert empty[1]["nextAfterRevision"] is None
    assert first_page["claimType"]=="ALL"  # backward-compatible old request
    for bad in ("afterRevision=0&limit=2&expectedCurrentRevision=3&claimType=URGENT",
        "afterRevision=0&limit=2&expectedCurrentRevision=3&claimType=contact",
        "afterRevision=0&limit=2&expectedCurrentRevision=3&claimType=SOURCE&claimType=CONTACT",
        "afterRevision=0&limit=2&expectedCurrentRevision=3&claimType=ALL&clinical=true"):
        bad_status,denied=history(bad)
        assert bad_status==400 and denied["nationalReleaseAllowed"] is False,(bad,bad_status,denied)
    status,stale=history("afterRevision=0&limit=2&expectedCurrentRevision=2")
    assert status==409 and stale["outcome"]=="TEST_CARE_HISTORY_REVISION_CHANGED"
    assert stale["nationalReleaseAllowed"] is False
    print("PASS: genuine PostgreSQL nonclinical coordination 3 event/audit claims, pinned event history, race/CAS, replay, no actual referral or service")
if __name__=="__main__":main()
