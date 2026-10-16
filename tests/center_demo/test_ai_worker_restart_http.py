#!/usr/bin/env python3
"""TRN-TECH-153: actual Kestrel process restart and volatile telemetry loss.

Called only by the isolated 14-job CI harness with 'before' and 'after' phases.
The witness file includes NO sensitive identifiers, host credential or model.
"""
from __future__ import annotations
import json
from pathlib import Path
import re
import sys
from urllib.request import Request, urlopen
from urllib.error import HTTPError

BASE="http://127.0.0.1:4320/internal/demo/v1/ai-worker/"
COUNTERS=("started","success","processRejected","invalidReply",
          "deadlineExceeded","requestCancelled","unavailable",
          "capacityDenied","inFlight","peakInFlight")


def get(relative):
    req=Request(BASE+relative,method="GET",headers={"Accept":"application/json"})
    with urlopen(req,timeout=8) as response:
        assert response.headers.get("Cache-Control") == "no-store"
        return response.status,json.loads(response.read())


def check(response):
    assert response["contract"]=="TRN-TECH-149",response
    assert response["outcome"]=="EPHEMERAL_WORKER_COUNTERS_ONLY",response
    assert response["syntheticOnly"] is True
    for flag in ("adminAuthenticated","operatorAuthorized",
                 "committeeApproved","trainingAllowed","productionAllowed"):
        assert response[flag] is False,(flag,response)
    d=response["data"]
    assert d["auditEvidence"] is False
    assert d["operationalIdentityVerified"] is False
    assert d["productionReady"] is False
    assert d["continuityAttested"] is False
    assert d["counterResetOnRestart"] is True
    assert re.fullmatch("[0-9a-f]{32}",d["instanceEpoch"]),d
    assert all(type(d[k]) is int and d[k]>=0 for k in COUNTERS),d
    assert set(d)==set(COUNTERS)|{
        "instanceEpoch","auditEvidence","operationalIdentityVerified",
        "productionReady","continuityAttested","counterResetOnRestart"
    },d
    return d


def post_inventory():
    req=Request(BASE+"inventory",data=json.dumps({
        "requestRef":"test:restart-read",
        "tenantRef":"test:team-1"
    }).encode("utf-8"),headers={"Content-Type":"application/json"},method="POST")
    with urlopen(req,timeout=10) as response:
        value=json.loads(response.read())
        assert response.status==200,value
        assert value["productionAllowed"] is False
        assert value["trainingAllowed"] is False
        assert value["data"]["counts"] == {
            "datasets":0,"modelVersions":0,"evaluations":0,
            "trainingRuns":0,"benchmarks":0
        },value


def main():
    assert len(sys.argv)==3 and sys.argv[1] in ("before","after")
    mode=sys.argv[1]
    witness=Path(sys.argv[2])
    assert witness.is_absolute() and witness.parent.name.startswith("taranom-worker-")
    status,data=get("telemetry")
    assert status==200
    current=check(data)
    if mode=="before":
        assert current["started"]>=5
        assert current["success"]>=5
        assert current["inFlight"]==0
        witness.write_text(json.dumps({
            "instanceEpoch":current["instanceEpoch"],
            "started":current["started"],
            "success":current["success"]
        }),encoding="utf-8")
        print("PASS: pre-restart volatile synthetic counters observed")
        return
    old=json.loads(witness.read_text(encoding="utf-8"))
    assert re.fullmatch("[0-9a-f]{32}",old["instanceEpoch"])
    assert old["started"]>0 and old["success"]>0
    assert current["instanceEpoch"] != old["instanceEpoch"],(
        "STILL_SAME_PROCESS_OR_STALE_TELEMETRY",current)
    assert all(current[k]==0 for k in COUNTERS),current
    post_inventory()
    _,later=get("telemetry")
    fresh=check(later)
    assert fresh["instanceEpoch"]==current["instanceEpoch"]
    assert fresh["started"]==1 and fresh["success"]==1, fresh
    assert fresh["inFlight"]==0 and fresh["peakInFlight"]==1, fresh
    assert fresh["capacityDenied"]==0,fresh
    for k in ("processRejected","invalidReply","deadlineExceeded",
              "requestCancelled","unavailable"):
        assert fresh[k]==0,fresh
    serialized=json.dumps(later)
    for secret in ("test:restart-read","test:team-1","requestRef",
                   "tenantRef","mac","Authorization","Bearer"):
        assert secret not in serialized,secret
    try:
        get("telemetry?tenantRef=test:other")
    except HTTPError as exc:
        assert exc.code==403
    else:
        raise AssertionError("FORGED_TELEMETRY_QUERY_ACCEPTED")
    print("PASS: actual Kestrel restart detected; old counts intentionally lost, "
          "new epoch starts empty and read-only worker recovers")


if __name__ == "__main__":
    main()
