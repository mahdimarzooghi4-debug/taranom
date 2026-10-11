#!/usr/bin/env python3
"""Real .NET loopback HTTP integration, no mocked request library or external data."""
import json
import sys
from urllib.error import HTTPError
from urllib.request import Request, urlopen

BASE = "http://127.0.0.1:4318"


def call(path, method="GET"):
    try:
        with urlopen(Request(BASE + path, method=method), timeout=5) as response:
            return response.status, response.headers, response.read()
    except HTTPError as error:
        return error.code, error.headers, error.read()


def run():
    status, headers, raw = call("/internal/ops/v1/status")
    assert status == 200
    result = json.loads(raw)
    assert result["contract"] == "TRN-TECH-071"
    assert result["runtime"] == "LOOPBACK_PROCESS_ALIVE"
    assert result["inventory"] == "SOURCE_BLOCKED_UNVERIFIED"
    assert result["releaseAllowed"] is False
    assert result["verifiedIntegrations"] is False
    assert len(result["dimensions"]) == 17
    assert len({d["id"] for d in result["dimensions"]}) == 17
    assert all(d["status"] == "BLOCKED_UNVERIFIED" and d["missingEvidence"] for d in result["dimensions"])
    assert headers.get("Cache-Control") == "no-store"
    assert headers.get("Access-Control-Allow-Origin") is None
    assert "default-src 'none'" in headers.get("Content-Security-Policy", "")
    assert call("/internal/ops/v1/status", "POST")[0] == 405
    assert call("/operations")[0] == 200
    assert json.loads(call("/internal/ops/v1/status")[2])["releaseAllowed"] is False
    print("PASS: real loopback HTTP + static React + 17 denied release dimensions")




def waiting_preview(activation, observed, extras=None):
    body = {"activationUtc": activation, "observedUtc": observed}
    if extras:
        body.update(extras)
    req = Request(BASE + "/internal/ops/v1/waiting-preview",
                  method="POST",
                  data=json.dumps(body).encode("utf-8"),
                  headers={"Content-Type": "application/json"})
    try:
        with urlopen(req, timeout=5) as response:
            return response.status, json.loads(response.read())
    except HTTPError as error:
        return error.code, json.loads(error.read())


def verify_waiting_preview():
    start = "2026-01-01T00:00:00Z"
    done = "2026-04-01T00:00:00Z"
    code, result = waiting_preview(start, done)
    assert code == 200 and result["state"] == "NinetyFullDaysElapsed"
    assert result["thresholdUtc"] == done
    assert result["hypotheticalOnly"] is True
    for permission in ("officialActivationVerified", "memberEligibilityVerified",
                       "walletCreditAuthorized", "nationalReleaseAllowed"):
        assert result[permission] is False
    code, value = waiting_preview(start, "2026-03-31T23:59:59Z")
    assert code == 200 and value["state"] == "StillWaiting"
    code, value = waiting_preview(None, done)
    assert code == 200 and value["state"] == "OfficialActivationMissing"
    assert value["thresholdUtc"] is None
    code, value = waiting_preview(start, "2025-12-31T23:59:59Z")
    assert code == 200 and value["state"] == "ObservationBeforeActivation"
    assert value["thresholdUtc"] is None
    for activation, observed, more in (
        ("2026-01-01T00:00:00+03:30", done, None),
        (start, "tomorrow", None),
        (start, done, {"memberId": "test:injected"}),
        (start, done, {"walletAmount": 1}),
        ("x" * 1500, done, None),
    ):
        code, error = waiting_preview(activation, observed, more)
        assert code == 400 and error["nationalReleaseAllowed"] is False
        assert error["walletCreditAuthorized"] is False
    assert call("/internal/ops/v1/status", "POST")[0] == 405
    print("PASS: hypothetical 90-day domain rule, exact boundary, no identity or wallet rights")


if __name__ == "__main__":
    try:
        run()
        verify_waiting_preview()
    except Exception:
        print("DENIED: REAL_LOCAL_OPS_HTTP_CONTRACT_UNVERIFIED", file=sys.stderr)
        raise SystemExit(1)
