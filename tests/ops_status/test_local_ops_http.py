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


if __name__ == "__main__":
    try:
        run()
    except Exception:
        print("DENIED: REAL_LOCAL_OPS_HTTP_CONTRACT_UNVERIFIED", file=sys.stderr)
        raise SystemExit(1)
