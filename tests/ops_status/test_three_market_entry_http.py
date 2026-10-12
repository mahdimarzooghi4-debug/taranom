#!/usr/bin/env python3
"""TRN-TECH-081 real loopback HTTP tests; no identity or external integrations."""
import json
from urllib.error import HTTPError
from urllib.request import Request, urlopen

BASE = "http://127.0.0.1:4318"
MARKETS = {"MARKET-PUBLIC", "MARKET-SUPPORT", "MARKET-CORPORATE"}
ROUTES = ("/markets", "/markets/individuals", "/markets/support",
          "/markets/corporate")


def call(path, method="GET", headers=None, data=None):
    try:
        with urlopen(Request(BASE + path, method=method, headers=headers or {},
                             data=data), timeout=8) as result:
            return result.status, result.headers, result.read()
    except HTTPError as ex:
        return ex.code, ex.headers, ex.read()


def run():
    status, headers, raw = call("/internal/ops/v1/market-entry")
    assert status == 200 and headers.get("Cache-Control") == "no-store"
    assert headers.get("Access-Control-Allow-Origin") is None
    result = json.loads(raw)
    assert result["contract"] == "TRN-TECH-081"
    assert result["source"] == "PUBLIC_17_DENIAL_INVENTORY"
    assert result["marketCount"] == 3 and result["blockerCount"] == 17
    assert len(result["markets"]) == 3
    assert {m["id"] for m in result["markets"]} == MARKETS
    for key in ("releaseAllowed", "nationalReleaseAllowed",
                "onboardingEnabled", "memberAuthenticated",
                "sponsorClinicalAccessAuthorized", "paymentsEnabled",
                "providerIntegrationsVerified", "officialMembershipActivated"):
        assert result[key] is False
    for m in result["markets"]:
        assert m["status"] == "BLOCKED_UNVERIFIED"
        assert m["ownershipAlwaysIndividual"] is True
        assert m["sponsorClinicalAccessAuthorized"] is False
        assert m["allowedActions"] == []
        assert m["missingEvidence"]
        assert m["thirdPartyPayerContractRequired"] == (m["id"] != "MARKET-PUBLIC")
    for route in ROUTES:
        status, headers, body = call(route)
        assert status == 200 and b"<html" in body.lower()
        assert headers.get("Cache-Control") == "no-store"
        assert call(route, "POST")[0] == 405
    for route in ("/markets/login", "/markets/corporate/register",
                  "/api/v1/subscribe", "/market/onboard", "/markets/x"):
        status, _, body = call(route)
        assert status == 404 and b"<html" not in body.lower()
    assert call("/internal/ops/v1/market-entry", "POST")[0] == 405
    invalid, _, body = call("/internal/ops/v1/market-entry?memberId=test:spoof")
    assert invalid == 400 and json.loads(body)["releaseAllowed"] is False
    for bad in ({"Host": "attacker.example:4318"},
                {"X-Forwarded-Host": "attacker.example"}):
        denied, _, raw = call("/internal/ops/v1/market-entry", headers=bad)
        assert denied == 403
        value = json.loads(raw)
        assert value["nationalReleaseAllowed"] is False
    print("PASS: .NET/React three-market informational entry, 17 blocked, Host/route denial")


if __name__ == "__main__":
    run()
