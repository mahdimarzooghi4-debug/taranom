#!/usr/bin/env python3
"""TRN-TECH-093: genuine Kestrel to canonical .NET builder, strictly ephemeral."""
import json
import uuid
from urllib.error import HTTPError
from urllib.request import Request, urlopen

BASE = "http://127.0.0.1:4318/internal/ops/v1/synthetic-cost-workbench"
LANES = ("ProviderService", "PrimaryInsurance", "SupplementalInsurance",
         "TreatmentCredit", "MemberRemainder")
ORIGIN = "http://127.0.0.1:4318"


def body():
    return {
        "memberRef": "test:member84", "previewId": str(uuid.uuid4()),
        "previewRevision": 1,
        "lines": [{"component": k, "state": "Missing", "amount": None,
                   "currency": None, "sourceRef": None, "evidenceRef": None,
                   "sourceRevision": 0} for k in LANES],
    }


def send(value, headers=None, raw=None, method="POST"):
    payload = raw if raw is not None else json.dumps(value).encode()
    req = Request(BASE, data=payload if method == "POST" else None,
                  method=method, headers={
                      "Content-Type": "application/json",
                      "Origin": ORIGIN, **(headers or {})
                  })
    try:
        with urlopen(req, timeout=8) as res:
            return res.status, json.loads(res.read()), res.headers
    except HTTPError as err:
        raw_error = err.read()
        return err.code, json.loads(raw_error) if raw_error.strip() else {}, err.headers


def claim(line, amount="80"):
    line.update(state="ProvisionalClaim", amount=amount, currency="IRR",
                sourceRef="test:" + line["component"],
                evidenceRef=str(uuid.uuid4()), sourceRevision=1)


def assert_no_rights(result):
    for key in ("memberAuthenticated", "officialEstimateVerified",
                "insurerEntitlementVerified", "choiceCarriedForward",
                "financialCommitmentCreated", "clinicalConsentGranted",
                "walletMutated", "nationalReleaseAllowed"):
        assert result[key] is False, (key, result)


def run():
    original = body()
    status, result, headers = send(original)
    assert status == 200 and result["state"] == "MissingOrIncomplete", result
    assert result["contract"] == "TRN-TECH-093" and result["syntheticOnly"] is True
    assert result["fingerprint"] and len(result["fingerprint"]) == 64
    assert [x["component"] for x in result["lines"]] == list(LANES)
    assert all(x["amount"] is None for x in result["lines"])
    assert headers.get("Access-Control-Allow-Origin") is None
    assert_no_rights(result)
    assert send(original)[1]["fingerprint"] == result["fingerprint"]

    for item in original["lines"]:
        claim(item)
    status, all_provisional, _ = send(original)
    assert status == 200 and all_provisional["state"] == "AllComponentsProvisionalOnly"
    assert len(all_provisional["lines"]) == 5
    assert all(x["amount"] == "80" for x in all_provisional["lines"])
    assert_no_rights(all_provisional)
    old_digest = all_provisional["fingerprint"]
    original["previewRevision"] = 2
    assert send(original)[1]["fingerprint"] != old_digest
    original["lines"][2].update(state="ConflictingClaims", amount=None, currency=None)
    status, conflicted, _ = send(original)
    assert status == 200 and conflicted["state"] == "Conflicted"
    assert conflicted["lines"][2]["amount"] is None
    assert_no_rights(conflicted)
    print("PASS: five source .NET states, fingerprints and never carried authority")

    denied_cases = []
    def bad(mutator):
        x = body()
        mutator(x)
        denied_cases.append(x)
    bad(lambda x: x.update(memberRef="real:member"))
    bad(lambda x: x.update(previewRevision=0))
    bad(lambda x: x.update(previewRevision=1.5))
    bad(lambda x: x.update(walletMutated=True))
    bad(lambda x: x.update(clinicalConsentGranted=True))
    bad(lambda x: x["lines"].append(x["lines"][0]))
    bad(lambda x: x["lines"].__setitem__(4, x["lines"][0]))
    bad(lambda x: x["lines"][0].update(component="1"))
    bad(lambda x: x["lines"][0].update(component="NotALane"))
    bad(lambda x: x["lines"][0].update(amount="0"))
    bad(lambda x: x["lines"][0].update(sourceRef="test:madeup"))
    bad(lambda x: claim(x["lines"][0], "-2"))
    bad(lambda x: (claim(x["lines"][0]), x["lines"][0].update(sourceRef="real:provider")))
    bad(lambda x: (claim(x["lines"][0]), x["lines"][0].update(evidenceRef="bad-uuid")))
    bad(lambda x: (claim(x["lines"][0]), x["lines"][0].update(amount="not-a-number")))
    bad(lambda x: (claim(x["lines"][0]), x["lines"][0].update(currency="irr")))
    bad(lambda x: (claim(x["lines"][0]), x["lines"][0].update(state="ConflictingClaims")))
    bad(lambda x: x["lines"][0].update(unexpected=True))
    for candidate in denied_cases:
        code, error, _ = send(candidate)
        assert code == 400 and error["outcome"] == "INVALID_SYNTHETIC_COST_INPUT", (code, candidate, error)
        assert_no_rights(error)
    for kwargs in (
        {"headers": {"Origin": "https://hostile.example"}},
        {"headers": {"Origin": ""}},
        {"headers": {"Host": "hostile.example:4318"}},
        {"headers": {"Forwarded": "host=localhost"}},
        {"raw": b'{"memberRef":"test:m","memberRef":"test:m"}'},
        {"raw": b"{" + b"a" * 8200 + b"}"},
        {"raw": b'{"lines":{}, "memberRef":"test:m"}'},
    ):
        code, _, _ = send(body(), **kwargs)
        assert code in (400, 403), (code, kwargs)
    assert send(body(), method="GET")[0] == 405
    print("PASS: strict hostile origins, Host/forwarding, malformed fields and rights fail closed")


if __name__ == "__main__":
    run()
