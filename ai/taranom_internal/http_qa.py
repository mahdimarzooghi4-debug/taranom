"""Loopback-only synthetic Taranom AI metadata QA API and local workbench.

This is not authenticated admin, a production HTTP service, clinical AI,
dataset permission, model loader, or a deployment endpoint. Both hard gates:
CI_EPHEMERAL_ONLY and fixed 127.0.0.1 listener.
"""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import re
from urllib.parse import parse_qs, urlsplit

from .registry import OfflineRegistry, RegistryRejected

API="/internal/qa/v1/ai"
MAX_BODY=65536

def _no_duplicate_keys(pairs):
    value={}
    for key,item in pairs:
        if key in value:
            raise RegistryRejected("DUPLICATE_JSON_KEY")
        value[key]=item
    return value

class QaHandler(BaseHTTPRequestHandler):
    server_version="TaranomAiTestOnly"
    sys_version=""
    protocol_version="HTTP/1.0"

    def log_message(self,fmt,*args):
        # Requests can contain synthetic references. Never log user values.
        return

    def _authority(self):
        port=self.server.server_port
        host=f"127.0.0.1:{port}"
        # Host/forwarding/cookie and auth headers do NOT authorize actors.
        if self.client_address[0]!="127.0.0.1" or self.headers.get_all("Host")!=[host]:
            return False
        forbidden=("Authorization","Cookie","Forwarded","X-Forwarded-Host",
                   "X-Forwarded-Proto","X-Forwarded-For","X-Tenant",
                   "X-Organization","Proxy-Authorization")
        if any(self.headers.get_all(k) for k in forbidden):
            return False
        return True

    def _send(self,code,body,content_type="application/json; charset=utf-8"):
        data=(json.dumps(body,sort_keys=True).encode("utf-8")
              if type(body) is dict else body)
        self.send_response(code)
        self.send_header("Content-Type",content_type)
        self.send_header("Content-Length",str(len(data)))
        self.send_header("Cache-Control","no-store")
        self.send_header("X-Content-Type-Options","nosniff")
        self.send_header("X-Frame-Options","DENY")
        self.send_header("Referrer-Policy","no-referrer")
        self.send_header("Content-Security-Policy",
                         "default-src 'none'; script-src 'self'; "
                         "connect-src 'self'; base-uri 'none'; form-action 'none'")
        self.end_headers()
        self.wfile.write(data)

    def _envelope(self,outcome,data=None):
        return {"contract":"TRN-TECH-142","outcome":outcome,
                "syntheticOnly":True,"operatorAuthenticated":False,
                "adminAuthorized":False,"modelActivated":False,
                "trainingAllowed":False,"productionAllowed":False,
                "data":data}

    def _reject(self,status,reason):
        self._send(status,self._envelope(reason))

    def do_GET(self):
        if not self._authority():
            return self._reject(403,"UNTRUSTED_TEST_AUTHORITY")
        url=urlsplit(self.path)
        if url.path in ("/ai-qa","/ai-qa/app.js") and not url.query:
            suffix="index.html" if url.path=="/ai-qa" else "app.js"
            path=Path(__file__).resolve().parent/"web"/suffix
            body=path.read_bytes()
            return self._send(200,body,
                "text/html; charset=utf-8" if suffix.endswith("html")
                else "text/javascript; charset=utf-8")
        if url.path!=API+"/inventory":
            return self._reject(404,"NO_TEST_ENDPOINT")
        try:
            q=parse_qs(url.query,strict_parsing=True,keep_blank_values=True)
            if set(q)!={"tenant"} or len(q["tenant"])!=1:
                raise RegistryRejected("EXACT_TENANT_QUERY_REQUIRED")
            result=self.server.registry.read_inventory(q["tenant"][0])
            self._send(200,self._envelope("Read",result))
        except (RegistryRejected,ValueError) as exc:
            self._reject(400,str(exc))

    def do_POST(self):
        if not self._authority():
            return self._reject(403,"UNTRUSTED_TEST_AUTHORITY")
        origin=f"http://127.0.0.1:{self.server.server_port}"
        if self.headers.get_all("Origin")!=[origin]:
            return self._reject(403,"SAME_ORIGIN_REQUIRED")
        route=urlsplit(self.path)
        commands={
            API+"/datasets":"freeze_dataset",
            API+"/models":"register_model",
            API+"/evaluations":"evaluate_synthetic",
        }
        if route.path not in commands or route.query:
            return self._reject(404,"NO_TEST_ENDPOINT")
        if (self.headers.get_all("Content-Type")!=["application/json"] or
            self.headers.get("Transfer-Encoding") is not None or
            len(self.headers.get_all("Content-Length") or [])!=1):
            return self._reject(415,"STRICT_JSON_BODY_REQUIRED")
        raw_length=self.headers["Content-Length"]
        if not re.fullmatch(r"[1-9][0-9]{0,4}",raw_length):
            return self._reject(413,"UNTRUSTED_CONTENT_LENGTH")
        length=int(raw_length)
        if length>MAX_BODY:
            return self._reject(413,"BODY_TOO_LARGE")
        try:
            raw=self.rfile.read(length)
            payload=json.loads(raw.decode("utf-8"),object_pairs_hook=_no_duplicate_keys)
            result=getattr(self.server.registry,commands[route.path])(payload)
            self._send(200,self._envelope("RecordedUnverified",result))
        except RegistryRejected as exc:
            code=409 if any(k in str(exc) for k in
                ("CONFLICTING","ALREADY_REGISTERED","ALREADY_FROZEN",
                 "ALREADY_RECORDED")) else 400
            self._reject(code,str(exc))
        except (UnicodeDecodeError,ValueError):
            self._reject(400,"INVALID_JSON_PAYLOAD")
        except Exception:
            # No raw DB/internal stack traces, secrets or source values.
            self._reject(503,"LOCAL_TEST_SERVICE_UNAVAILABLE")

    def do_OPTIONS(self):
        self._reject(405,"CORS_PREFLIGHT_NOT_SUPPORTED")

def make_server(database,port=0):
    if os.environ.get("TARANOM_AI_OFFLINE_TEST_ONLY")!="CI_EPHEMERAL_ONLY":
        raise RegistryRejected("EPHEMERAL_AI_TEST_MODE_REQUIRED")
    if type(port) is not int or not 0<=port<=65535:
        raise RegistryRejected("INVALID_LOOPBACK_PORT")
    r=OfflineRegistry(database)
    r.initialize()
    server=ThreadingHTTPServer(("127.0.0.1",port),QaHandler)
    server.daemon_threads=True
    server.registry=r
    return server

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--db",required=True)
    p.add_argument("--port",type=int,default=4377)
    a=p.parse_args(argv)
    with make_server(a.db,a.port) as server:
        print("Taranom synthetic AI QA: http://127.0.0.1:%s/ai-qa"
              % server.server_port,flush=True)
        server.serve_forever()

if __name__=="__main__":
    main()
