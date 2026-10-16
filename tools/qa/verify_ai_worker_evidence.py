#!/usr/bin/env python3
"""Independent CI-only check of .NET append-only synthetic worker evidence.

Hash linkage is error/tamper-evident against uncoordinated changes, not
tamper-proof against a writer re-hashing the complete file. Not legal audit.
"""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import re
from urllib.request import urlopen

GENESIS="0"*64
KINDS={"Boot","Success","ProcessRejected","InvalidReply","DeadlineExceeded",
       "RequestCancelled","Unavailable","CapacityDenied"}
FIELDS={"sequence","epoch","kind","previousDigest","digest"}
def unique(pairs):
    result={}
    for k,v in pairs:
        assert k not in result, "DUPLICATE_FIELD"
        result[k]=v
    return result

def verify(path):
    assert path.name=="worker-evidence.jsonl"
    assert path.parent.parent==Path("/tmp")
    assert path.parent.name.startswith("taranom-worker-")
    assert not path.is_symlink()
    data=path.read_bytes()
    assert 0<len(data)<=524288 and data.endswith(b"\n"),"PARTIAL_EVIDENCE"
    rows=data[:-1].decode("utf-8",errors="strict").split("\n")
    assert 2<len(rows)<=4096
    prev=GENESIS
    boots=[]
    last=None
    for seq,row in enumerate(rows,1):
        assert len(row)<512
        value=json.loads(row,object_pairs_hook=unique)
        assert type(value) is dict and set(value)==FIELDS
        assert type(value["sequence"]) is int and value["sequence"]==seq
        epoch=value["epoch"]
        kind=value["kind"]
        digest=value["digest"]
        assert type(epoch) is str and re.fullmatch("[0-9a-f]{32}",epoch)
        assert type(kind) is str and kind in KINDS
        assert type(digest) is str and re.fullmatch("[0-9A-F]{64}",digest)
        assert value["previousDigest"]==prev
        raw=json.dumps(value,separators=(",",":"),ensure_ascii=False)
        assert raw==row,"NONCANONICAL_EVIDENCE"
        expected=hashlib.sha256(
            ("TRN-TECH-154\n"+str(seq)+"\n"+epoch+"\n"+kind+"\n"+prev).encode()
        ).hexdigest().upper()
        assert digest==expected,"BROKEN_HASH_LINK"
        if kind=="Boot":
            assert epoch!=last
            boots.append(epoch)
        else:
            assert last==epoch
        last=epoch
        prev=digest
    return rows,boots,last

def main():
    raw=os.environ["TARANOM_AI_WORKER_EVIDENCE_FILE"]
    path=Path(raw)
    rows,boots,last=verify(path)
    assert len(boots)==2 and boots[0]!=boots[1], "MISSING_REAL_RESTART"
    # One synthetic worker recovery succeeds in the new epoch.
    assert '"kind":"Success"' in rows[-1] and '"epoch":"'+boots[1]+'"' in rows[-1]
    with urlopen("http://127.0.0.1:4320/internal/demo/v1/ai-worker/telemetry",
                 timeout=5) as response:
        current=json.load(response)
    assert current["data"]["instanceEpoch"]==last
    assert current["data"]["auditEvidence"] is False
    assert current["data"]["continuityAttested"] is False
    assert current["data"]["productionReady"] is False
    for forbidden in ("requestRef","tenantRef","Bearer","patient","token","modelRef",
                      "TOP_SECRET_TEST_SENTINEL"):
        assert forbidden not in path.read_text(),forbidden
    print("PASS: independent SHA-256 evidence verification across two real Kestrel epochs; "
          "not legal audit or Production admission")

if __name__=="__main__":
    main()
