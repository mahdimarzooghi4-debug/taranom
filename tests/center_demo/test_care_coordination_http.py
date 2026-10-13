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
    print("PASS: genuine PostgreSQL nonclinical coordination 3 event/audit claims, race/CAS, replay, no actual referral or service")
if __name__=="__main__":main()
