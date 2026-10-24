#!/usr/bin/env python3
"""Real Kestrel partial-body synthetic case command timeout, zero journal writes.

Sends headers declaring a larger payload, writes an incomplete JSON prefix and
keeps the socket OPEN. Requires a bounded, non-successful answer without creating
a case or an event. Runs four case flows concurrently within isolated CI.
"""
import json
import socket
import time
from concurrent.futures import ThreadPoolExecutor
from urllib.request import urlopen
from uuid import uuid4

BASE="http://127.0.0.1:4320"
TARGETS=(
    ("care-coordination","revision"),
    ("support","revision"),
    ("proofing","caseRevision"),
    ("late-evidence","revision"),
)
def attempt(name):
    ref="test:ingress-timeout-"+uuid4().hex
    route="/internal/demo/v1/"+name+"/"+ref
    path=route+"/commands"
    sock=socket.create_connection(("127.0.0.1",4320),timeout=8)
    try:
        sock.settimeout(8)
        head=(
            f"POST {path} HTTP/1.1\r\n"
            "Host: 127.0.0.1:4320\r\n"
            "Origin: http://127.0.0.1:4320\r\n"
            "Content-Type: application/json\r\n"
            "Content-Length: 300\r\n"
            "Connection: close\r\n\r\n"
        ).encode("ascii")
        sock.sendall(head+b'{"requestId":"')
        started=time.monotonic()
        chunks=[]
        while True:
            fragment=sock.recv(4096)
            if not fragment:break
            chunks.append(fragment)
            if sum(len(x) for x in chunks)>65536:raise AssertionError("oversized response")
        elapsed=time.monotonic()-started
        raw=b"".join(chunks)
        assert raw.startswith(b"HTTP/1.1 "), (name,raw[:130])
        status=int(raw.split(b" ",2)[1])
        # 400/408 can originate from Kestrel's own HTTP input detection.
        # 503 is the application fail-closed deadline response.
        assert status in (400,408,503),(name,status,raw[:450])
        assert elapsed<7.5,(name,elapsed)
    finally:
        sock.close()
    with urlopen(BASE+route,timeout=8) as response:
        assert response.status==200
        state=json.loads(response.read())
    assert state["contract"].startswith("TRN-TECH-")
    if name=="support":
        assert state["followUps"]==[] and state["followUpCount"]==0,(name,state)
    else:
        assert state["events"]==[],(name,state)
    assert state["nationalReleaseAllowed"] is False
    if name=="proofing":
        assert state["caseRevision"]==0
    else:
        assert state["revision"]==0
    return name
def main():
    with ThreadPoolExecutor(max_workers=4) as pool:
        names=list(pool.map(attempt,(name for name,_ in TARGETS)))
    assert set(names)=={x[0] for x in TARGETS}
    print("PASS: 4 live Kestrel/PG synthetic commands reject incomplete slow body before DB mutation")
if __name__=="__main__":
    main()
