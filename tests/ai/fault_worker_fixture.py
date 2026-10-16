"""TRN-TECH-151: adversarial Python CHILD fixture, not a service or API.

This script is reachable only from the separate .NET executable fault probe.
The real HTTP handler never accepts a fixture name, action or fault switch.
"""
import hashlib
import hmac
import json
import os
import sys
import time

def signed(key, *fields):
    return hmac.new(key,"\\n".join(map(str,fields)).encode(),
                    hashlib.sha256).hexdigest().upper()

def main():
    mode=sys.argv[1]
    if mode=="die-early":
        return 17
    request=json.loads(sys.stdin.buffer.read(1025))
    key=bytes.fromhex(os.environ["TARANOM_FAULT_PROBE_KEY"])
    counts={k:0 for k in ("datasets","modelVersions","evaluations",
                           "trainingRuns","benchmarks")}
    reply={
        "contract":"TRN-TECH-149","command":"READ_SYNTHETIC_INVENTORY",
        "requestRef":request["requestRef"],"tenantRef":request["tenantRef"],
        "nonce":request["nonce"],"status":"SYNTHETIC_READ_ONLY",
        "counts":counts,"syntheticOnly":True,"adminAuthenticated":False,
        "trainingAllowed":False,"productionAllowed":False
    }
    reply["mac"]=signed(key,reply["contract"],reply["command"],
        reply["requestRef"],reply["tenantRef"],reply["nonce"],reply["status"],
        "true","false","false","false",*(counts[k] for k in counts))
    body=json.dumps(reply,separators=(",",":")).encode()
    if mode=="silence":
        return 0
    if mode=="bad-json":
        sys.stdout.buffer.write(b"{broken")
        return 0
    if mode=="invalid-utf8":
        sys.stdout.buffer.write(b"\\xff\\xfe{}")
        return 0
    if mode=="overflow-stdout":
        sys.stdout.buffer.write(b"x"*3000)
        return 0
    if mode=="stderr-leak":
        sys.stderr.write("TOP_SECRET_TEST_SENTINEL\\n")
    if mode=="overflow-stderr":
        sys.stderr.write("TOP_SECRET_TEST_SENTINEL"*300)
    if mode=="bad-proof":
        reply["mac"]="0"*64
        body=json.dumps(reply).encode()
    if mode=="wrong-tenant":
        reply["tenantRef"]="test:other-tenant"
        body=json.dumps(reply).encode()
    if mode=="trailing-garbage":
        body+=b"!!!"
    if mode=="sleep-before-output":
        time.sleep(3)
    if mode=="fragmented":
        for i in range(0,len(body),7):
            sys.stdout.buffer.write(body[i:i+7])
            sys.stdout.flush()
        return 0
    sys.stdout.buffer.write(body)
    sys.stdout.flush()
    if mode=="hang-after-output":
        time.sleep(3)
    if mode=="valid-but-nonzero":
        return 17
    return 0

if __name__=="__main__":
    raise SystemExit(main())
