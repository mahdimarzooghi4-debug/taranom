#!/usr/bin/env python3
"""TRN-TECH-090: actual loopback payer boundary HTTP and deny tests."""
import json
from urllib.error import HTTPError
from urllib.request import Request,urlopen

URL="http://127.0.0.1:4318/internal/ops/v1/payer-scenario-preview"
def call(data=None,method="POST",headers=None,url=URL):
    raw=None if data is None else (data if isinstance(data,bytes) else json.dumps(data).encode())
    h={"Content-Type":"application/json"}
    h.update(headers or {})
    try:
        with urlopen(Request(url,method=method,headers=h,data=raw),timeout=8) as r:
            return r.status,r.headers,json.loads(r.read())
    except HTTPError as ex:
        raw=ex.read()
        return ex.code,ex.headers,json.loads(raw) if raw else {}

def run():
    for market in ("MARKET-PUBLIC","MARKET-SUPPORT","MARKET-CORPORATE"):
        payers=("MEMBER",) if market=="MARKET-PUBLIC" else ("MEMBER","SPONSOR")
        for payer in payers:
            code,headers,value=call({"marketId":market,"payerMode":payer})
            assert code==200 and headers.get("Cache-Control")=="no-store"
            assert headers.get("Access-Control-Allow-Origin") is None
            assert value["contract"]=="TRN-TECH-090"
            assert value["outcome"]=="HYPOTHETICAL_POLICY_ONLY"
            assert value["marketId"]==market and value["payerMode"]==payer
            assert value["hypotheticalOnly"] is True
            assert value["structurallyDescribedByBusinessPolicy"] is True
            assert value["sponsorContractRequired"]==(payer=="SPONSOR")
            assert value["individualOwnsMembership"] is True
            assert value["individualOwnsHealthcareWallet"] is True
            assert value["missingEvidence"]
            for k in ("sponsorContractVerified","payerVerified",
                      "sponsorClinicalAccessAuthorized","sponsorWalletControlAuthorized",
                      "splitPaymentTermsDefined","membershipActivated","paymentEnabled",
                      "onboardingEnabled","nationalReleaseAllowed"):
                assert value[k] is False,(market,payer,k)
    invalid=[
        {"marketId":"MARKET-PUBLIC","payerMode":"SPONSOR"},
        {"marketId":"MARKET-UNKNOWN","payerMode":"MEMBER"},
        {"marketId":"MARKET-SUPPORT","payerMode":"ADMIN"},
        {"marketId":"MARKET-CORPORATE","payerMode":"SPONSOR","contractVerified":True},
        {"marketId":4,"payerMode":"MEMBER"}
    ]
    for v in invalid:
        code,_,resp=call(v)
        assert code==400 and resp["nationalReleaseAllowed"] is False
    for raw in (
        b'{"marketId":"MARKET-SUPPORT","marketId":"MARKET-CORPORATE","payerMode":"SPONSOR"}',
        b'{"marketId":"MARKET-SUPPORT","payerMode":"SPONSOR"}{}',
        b'{"marketId":"MARKET-SUPPORT","payerMode":"SPONSOR"'+b' '*260+b'}'
    ):
        code,_,resp=call(raw)
        assert code==400 and resp["paymentEnabled"] is False
    assert call(method="GET")[0]==405
    assert call({"marketId":"MARKET-SUPPORT","payerMode":"SPONSOR"},
                headers={"Host":"evil.example:4318"})[0]==403
    assert call({"marketId":"MARKET-SUPPORT","payerMode":"SPONSOR"},
                headers={"Origin":"https://attacker.example"})[0]==400
    assert call({"marketId":"MARKET-SUPPORT","payerMode":"SPONSOR"},
                url=URL+"?verified=true")[0]==400
    print("PASS: five real payer cases, no transfer of member wallet or clinical authority")

if __name__=="__main__":run()
