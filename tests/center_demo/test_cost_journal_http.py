#!/usr/bin/env python3
"""Actual test-only PostgreSQL journal over Kestrel 4320 HTTP, no mocks."""
import copy
import json
from uuid import uuid4
from concurrent.futures import ThreadPoolExecutor
from urllib.error import HTTPError
from urllib.request import Request, urlopen

BASE="http://127.0.0.1:4320/internal/demo/v1/cost-previews"
ORIGIN="http://127.0.0.1:4320"
COMPONENTS=("ProviderService","PrimaryInsurance","SupplementalInsurance",
            "TreatmentCredit","MemberRemainder")


def make_preview():
    return {"memberRef":"test:pg-cost-"+uuid4().hex,
            "previewId":str(uuid4()),"previewRevision":1,
            "lines":[{"component":k,"state":"Missing","amount":None,
                      "currency":None,"sourceRef":None,"evidenceRef":None,
                      "sourceRevision":0} for k in COMPONENTS]}


def call(path="",body=None,origin=ORIGIN,method=None,raw=None,headers=None):
    if method is None: method="POST" if body is not None or raw is not None else "GET"
    data=raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    hdr={"Origin":origin,"Accept":"application/json",**(headers or {})}
    if data is not None:hdr["Content-Type"]="application/json"
    request=Request(BASE+path,method=method,data=data,headers=hdr)
    try:
        with urlopen(request,timeout=12) as resp:
            text=resp.read()
            return resp.status,json.loads(text) if text else {}
    except HTTPError as ex:
        text=ex.read()
        return ex.code,json.loads(text) if text else {}


def deny_flags(out):
    for key in ("memberAuthenticated","officialChoiceRecorded",
                "choiceSignatureValid","financialCommitmentCreated",
                "clinicalConsentGranted","walletMutated",
                "insurerEntitlementVerified","officialEstimateVerified",
                "nationalReleaseAllowed"):
        assert out[key] is False,key


def choice(p,fingerprint,revision=0,request=None):
    return {"requestId":request or str(uuid4()),"expectedRevision":revision,
            "memberRef":p["memberRef"],"previewId":p["previewId"],
            "previewRevision":p["previewRevision"],
            "previewFingerprint":fingerprint,"choice":"AcknowledgedUncertainty"}


def main():
    p=make_preview()
    status,registered=call(body=p)
    assert status==200 and registered["outcome"]=="Registered",(status,registered)
    assert registered["choiceRevision"]==0 and registered["historyCount"]==0
    assert registered["previewId"]==p["previewId"]
    assert len(registered["previewFingerprint"])==64
    deny_flags(registered)
    status,replay=call(body=p)
    assert status==200 and replay["outcome"]=="Replayed"
    assert replay["previewFingerprint"]==registered["previewFingerprint"]
    deny_flags(replay)
    ref="/"+p["previewId"]
    status,read=call(ref)
    assert status==200 and read["outcome"]=="READ" and read["choiceRevision"]==0

    command=choice(p,registered["previewFingerprint"])
    status,committed=call(ref+"/choices",command)
    assert status==200 and committed["outcome"]=="RecordedUnverified"
    assert committed["historyCount"]==committed["choiceRevision"]==1
    deny_flags(committed)
    assert call(ref+"/choices",command)[1]["outcome"]=="Replayed"
    assert call(ref)[1]["choiceRevision"]==1

    altered=dict(command,choice="Deferred")
    status,result=call(ref+"/choices",altered)
    assert status==409 and result["outcome"]=="ConflictingReplay"
    status,result=call(ref+"/choices",choice(p,registered["previewFingerprint"],revision=0))
    assert status==409 and result["outcome"]=="StaleRevision"
    status,result=call(ref+"/choices",choice(p,"A"*64,revision=1))
    assert status==409 and result["outcome"]=="PreviewChanged"
    assert call(ref)[1]["choiceRevision"]==1

    other=copy.deepcopy(p);other["previewRevision"]=2
    status,conflict=call(body=other)
    assert status==409 and conflict["outcome"]=="CONFLICTING_IMMUTABLE_PREVIEW"
    deny_flags(conflict)
    missing=call("/"+str(uuid4()))
    assert missing[0]==404 and missing[1]["outcome"]=="TEST_PREVIEW_NOT_FOUND"

    # Two independent simultaneous commands for the same 0 revision must be
    # serialized by the actual row lock, not create duplicate events.
    parallel=make_preview()
    register=call(body=parallel)
    assert register[0]==200
    fp=register[1]["previewFingerprint"]
    cref="/"+parallel["previewId"]+"/choices"
    commands=[choice(parallel,fp) for _ in range(2)]
    with ThreadPoolExecutor(max_workers=2) as pool:
        outcomes=list(pool.map(lambda c:call(cref,c),commands))
    assert sorted((s,v["outcome"]) for s,v in outcomes)==[
        (200,"RecordedUnverified"),(409,"StaleRevision")],outcomes
    assert call("/"+parallel["previewId"])[1]["choiceRevision"]==1

    # Source conflicts and missing evidence are independent and never a
    # monetary total. The existing five-lane builder, not an API adapter,
    # validates the canonical signed-shaped (untrusted) claims.
    with_claim=make_preview()
    with_claim["lines"][0].update(state="ProvisionalClaim",amount="12.75",
        currency="IRR",sourceRef="test:fake-provider",
        evidenceRef=str(uuid4()),sourceRevision=1)
    with_claim["lines"][1].update(state="ConflictingClaims",
        sourceRef="test:disputed-insurer",evidenceRef=str(uuid4()),sourceRevision=1)
    status,validated=call(body=with_claim)
    assert status==200 and validated["outcome"]=="Registered"
    deny_flags(validated)

    rejected=[]
    def bad(modify):
        x=copy.deepcopy(make_preview());modify(x);rejected.append(x)
    bad(lambda x:x.update(memberRef="real:person"))
    bad(lambda x:x.update(insuranceApproved=True))
    bad(lambda x:x["lines"][0].update(amount="0"))
    bad(lambda x:x["lines"][0].update(sourceRef="test:forged"))
    bad(lambda x:x["lines"][0].update(component="1"))
    bad(lambda x:x["lines"].append(copy.deepcopy(x["lines"][0])))
    bad(lambda x:x.update(previewRevision=0))
    for x in rejected:
        code,result=call(body=x)
        assert code==400 and result["outcome"]=="INVALID_TEST_PREVIEW",(code,result)
        deny_flags(result)
    wrong=dict(command,claimPaid=True)
    assert call(ref+"/choices",wrong)[0]==400
    assert call(ref+"/choices",dict(command,memberRef="real:patient"))[0]==400
    assert call(ref+"/choices",dict(command,choice="1"))[0]==400
    assert call(ref+"/choices",dict(command,previewId=str(uuid4())))[0]==400
    assert call(body=p,origin="https://evil.example")[0]==403
    assert call(body=p,origin="")[0]==403
    assert call(body=p,headers={"Host":"evil.example:4320"})[0]==403
    assert call(body=p,headers={"X-Forwarded-Host":"evil.example"})[0]==403
    assert call(raw=b'{"memberRef":"test:x","memberRef":"test:y"}')[0]==400
    assert call(raw=b"{" + b"a"*8500 + b"}")[0]==400
    assert call(ref+"/choices",method="GET")[0] in (404,405)
    assert call(ref,method="PUT")[0] in (404,405)
    assert call(ref)[1]["choiceRevision"]==1
    print("PASS: real PostgreSQL durable cost choice, UUID recovery, 2-replica race, reattestation and fail-closed HTTP")


if __name__=="__main__":
    main()
