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



def verify_ops_host_and_route_denial():
    """Genuine ASP.NET localhost Host header, forwarding, route and no-rights tests."""
    from urllib.request import Request, urlopen
    from urllib.error import HTTPError
    denied = (
        ("/", {"Host": "hostile.example:4318"}),
        ("/operations", {"Host": "hostile.example:4318"}),
        ("/internal/ops/v1/status", {"Host": "attacker.example:4318"}),
        ("/internal/ops/v1/waiting-preview", {"Host": "127.0.0.1:9999"}),
        ("/internal/ops/v1/status", {"Forwarded": "host=hostile.example"}),
        ("/internal/ops/v1/status", {"X-Forwarded-Host": "hostile.example"}),
        ("/operations", {"X-Forwarded-For": "198.51.100.3"}),
        ("/operations", {"X-Forwarded-Proto": "https"}),
    )
    for path, headers in denied:
        req = Request(BASE + path, headers=headers)
        try:
            with urlopen(req, timeout=8) as accepted:
                raise AssertionError("HOST_TRUST_BYPASS:" + path + ":" + str(accepted.status))
        except HTTPError as error:
            assert error.code == 403, (path, error.code)
            result = json.loads(error.read())
            assert result["contract"] == "TRN-TECH-078"
            assert result["outcome"] == "UNTRUSTED_LOOPBACK_AUTHORITY"
            assert result["officialActivationVerified"] is False
            assert result["walletCreditAuthorized"] is False
            assert result["releaseAllowed"] is False
            assert result["nationalReleaseAllowed"] is False
            assert result["verifiedIntegrations"] is False
            assert error.headers.get("Cache-Control") == "no-store"
            assert error.headers.get("Access-Control-Allow-Origin") is None

    unknown = (
        "/login", "/api/v1/member", "/api/v1/members/test:123",
        "/internal/ops/v1/identity", "/internal/ops/v1/status/admin",
        "/operations/real", "/clinical/records", "/admin", "/member",
        "/member-demo/", "/center-demo", "/internal/demo/v1/support/test:123"
    )
    for path in unknown:
        status, headers, response = call(path)
        assert status == 404, (path, status)
        assert b"<html" not in response.lower(), path
        assert headers.get("Cache-Control") == "no-store"
    for path in ("/", "/operations"):
        status, headers, html = call(path)
        assert status == 200 and "text/html" in headers.get("Content-Type", "")
        assert "ترنم | اتصال فنی محلی".encode() in html
    # Both services own independent explicit localhost ports and their
    # technical paths; neither allows a guessed live protected operation.
    status, _, blocked = call("/internal/ops/v1/status")
    assert status == 200 and json.loads(blocked)["releaseAllowed"] is False
    print("PASS: ops service denies rebinding/forwarding, unknown routes, fake operations")


def verify_ops_denial_not_overridden_by_browser_claims():
    """Same actual .NET route rejects false member/wallet claims and foreign origin."""
    for extra in (
        {"memberId": "test:claimed"},
        {"officialActivationVerified": True},
        {"walletCreditAuthorized": True},
        {"releaseAllowed": True},
    ):
        body = {"activationUtc": "2026-01-01T00:00:00Z",
                "observedUtc": "2026-04-01T00:00:00Z", **extra}
        req = Request(
            BASE + "/internal/ops/v1/waiting-preview",
            method="POST",
            data=json.dumps(body).encode(),
            headers={"Content-Type": "application/json"})
        try:
            with urlopen(req, timeout=8) as response:
                raise AssertionError("FAKE_RIGHTS_ACCEPTED")
        except HTTPError as error:
            assert error.code == 400
            payload = json.loads(error.read())
            assert payload["nationalReleaseAllowed"] is False
            assert payload["walletCreditAuthorized"] is False

    req = Request(BASE + "/internal/ops/v1/waiting-preview",
                  method="POST",
                  data=json.dumps({"activationUtc": "2026-01-01T00:00:00Z",
                                   "observedUtc": "2026-04-01T00:00:00Z"}).encode(),
                  headers={"Content-Type": "application/json",
                           "Origin": "https://untrusted.example"})
    try:
        with urlopen(req, timeout=8) as response:
            raise AssertionError("CROSS_ORIGIN_PREVIEW_ACCEPTED")
    except HTTPError as error:
        assert error.code == 400
        assert json.loads(error.read())["walletCreditAuthorized"] is False
    print("PASS: local operations route cannot accept browser-claimed financial or member rights")

if __name__ == "__main__":
    try:
        run()
        verify_waiting_preview()
        verify_ops_host_and_route_denial()
        verify_ops_denial_not_overridden_by_browser_claims()
    except Exception:
        import traceback
        traceback.print_exc(file=sys.stderr)
        print("DENIED: REAL_LOCAL_OPS_HTTP_CONTRACT_UNVERIFIED", file=sys.stderr)
        raise SystemExit(1)
