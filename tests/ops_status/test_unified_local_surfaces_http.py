#!/usr/bin/env python3
"""TRN-TECH-087 real localhost ASP.NET and independently built React routes."""
from urllib.error import HTTPError
from urllib.request import Request, urlopen

BASE="http://127.0.0.1:4318"
MEMBER=("/member","/member/membership","/member/credit","/member/consents",
        "/member/care","/member/prevention","/member/records",
        "/member/costs","/member/billing","/member/support")
CENTER=("/center","/center/departments","/center/doctors",
        "/center/qualifications","/center/audit")

def fetch(path,method="GET",headers=None):
    try:
        with urlopen(Request(BASE+path, method=method,
                             headers=headers or {}),timeout=10) as r:
            return r.status,r.headers,r.read()
    except HTTPError as exc:
        return exc.code,exc.headers,exc.read()

def verify():
    for prefix,pages in (("/member-portal/",MEMBER),("/center-portal/",CENTER)):
        all_assets=set()
        for path in pages:
            code,headers,body=fetch(path)
            assert code==200,(path,code)
            assert b"<html" in body.lower()
            assert prefix.encode() in body,(path,"wrong asset base")
            # Only naked root-level asset URLs can collide with the
            # separately compiled Ops bundle; /member-portal/assets is valid.
            assert b'src="/assets/' not in body, (path, "shared script collision")
            assert b'href="/assets/' not in body, (path, "shared style collision")
            assert headers.get("Cache-Control")=="no-store"
            assert headers.get("X-Content-Type-Options")=="nosniff"
            assert headers.get("Access-Control-Allow-Origin") is None
            assert b"memberAuthenticated\":true" not in body
            assert fetch(path,"POST")[0]==405
            assert fetch(path,"DELETE")[0]==405
            # Cross-origin requests from other host must not resolve React HTML.
            assert fetch(path,headers={"Host":"evil.example:4318"})[0]==403
            assert fetch(path,headers={"X-Forwarded-Host":"evil.example"})[0]==403
            assets=[part.split(b'"')[0].decode() for part in body.split(b'src="')[1:]
                    if part.startswith(prefix.encode())]
            assert assets,(path,"missing independent script bundle")
            all_assets.update(assets)
        for asset in all_assets:
            code,headers,data=fetch(asset)
            assert code==200 and len(data)>100
            assert headers.get("Cache-Control")=="no-store"
        assert all(asset.startswith(prefix) for asset in all_assets)
    for path in ("/member/login","/member/real-user","/member/consents/real",
                 "/center/patients","/center/api/login","/center/real-doctor",
                 "/api/v1/member/login","/api/v1/member/wallet",
                 "/api/v1/centers/activation"):
        code,_,body=fetch(path)
        assert code==404,(path,code)
        assert b"<html" not in body.lower()
    for path in ("/member-portal/not-available","/center-portal/not-available"):
        assert fetch(path)[0]==404
    # The existing operations/status API continues to reject release claims.
    code,_,body=fetch("/internal/ops/v1/status")
    assert code==200 and b'"releaseAllowed":false' in body
    print("PASS: 15 real separate React entry paths, isolated assets, Host/route denial, no activation")

if __name__=="__main__":
    verify()
