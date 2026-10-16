#!/usr/bin/env python3
"""Real Kestrel -> bounded local child Python -> SQLite synthetic inventory."""
import json
from concurrent.futures import ThreadPoolExecutor
from urllib.request import Request, urlopen
from urllib.error import HTTPError

ROOT = "http://127.0.0.1:4320/internal/demo/v1/ai-worker/inventory"


def send(body=None, headers=None, raw=None):
    data = (raw if raw is not None else json.dumps(body).encode())
    req = Request(ROOT, data=data, method="POST", headers={
        "Content-Type": "application/json", **(headers or {})})
    try:
        with urlopen(req, timeout=12) as r:
            return r.status, json.loads(r.read())
    except HTTPError as e:
        return e.code, json.loads(e.read())


def check(value):
    assert value["contract"] == "TRN-TECH-149", value
    for field in ("adminAuthenticated", "operatorAuthorized",
                  "trainingAllowed", "productionAllowed", "committeeApproved"):
        assert value[field] is False
    assert value["syntheticOnly"] is True


def main():
    values = []
    for n in range(5):
        status, v = send({"requestRef": f"test:bridge-{n}",
                          "tenantRef": "test:team-1"})
        check(v)
        assert status == 200 and v["outcome"] == "SYNTHETIC_INVENTORY_OBSERVED", v
        assert v["data"]["counts"] == {
            "datasets": 0, "modelVersions": 0, "evaluations": 0,
            "trainingRuns": 0, "benchmarks": 0}, v
        assert v["data"]["workerStatus"] == "SYNTHETIC_READ_ONLY"
        assert "mac" not in json.dumps(v)
        values.append(v)
    # Synthetic read-only; the client never chooses a command, process, key or db.
    for bad in (
        {"requestRef": "real:patient", "tenantRef": "test:team-1"},
        {"requestRef": "test:probe", "tenantRef": "real:team"},
        {"requestRef": "test:probe", "tenantRef": "test:team-1",
         "operation": "AuthorizeProductionActivation"},
        {"requestRef": "test:probe", "tenantRef": "test:team-1",
         "adminApproved": True}
    ):
        s, v = send(bad)
        check(v)
        assert s == 400, (bad, v)
    for hdr in ({"Authorization": "Bearer forged"},
                {"Cookie": "admin=true"}, {"X-Tenant": "test:forged"},
                {"Origin": "https://evil.invalid"}):
        s, v = send({"requestRef": "test:probe", "tenantRef": "test:team-1"},
                    headers=hdr)
        check(v)
        assert s == 403, (hdr, v)
    s, v = send(raw=b'{"requestRef":"test:a","requestRef":"test:b","tenantRef":"test:t"}')
    check(v)
    assert s == 400, v
    # The child-process boundary has a hard four-slot capacity. Depending on
    # timing, a simultaneous burst either finishes read-only or fails closed.
    with ThreadPoolExecutor(max_workers=12) as pool:
        many = list(pool.map(lambda n: send({
            "requestRef": f"test:concurrent-{n}", "tenantRef": "test:team-1"}),
            range(24)))
    for s, r in many:
        check(r)
        assert (s == 200 and r["data"]["counts"]["datasets"] == 0) or (
            s == 503 and r["outcome"] == "WORKER_CAPACITY_EXCEEDED"), (s,r)
    assert any(s == 200 for s, _ in many), many
    print("PASS: signed local .NET/Python/SQLite synthetic inventory bridge; "
          "bounded input, denied authority, isolation and parallel calls")


if __name__ == "__main__":
    main()
