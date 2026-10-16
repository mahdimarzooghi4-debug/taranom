"""TRN-TECH-142: loopback-only synthetic AI QA HTTP and built React assets.

No authentication is claimed: startup requires an explicit CI-only opt-in.
Do not use it on shared servers or forward/reverse-proxy its port.
"""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
from urllib.parse import urlsplit, parse_qs, unquote

from .registry import OfflineRegistry, RegistryRejected, OFFLINE

PORT = 4335
BASE = "/internal/ai-test/v1"
CONTRACT = "TRN-TECH-142"
MAX_BODY = 32_768
ROOT = Path(__file__).resolve().parents[2]
ALLOWED_ASSET_SUFFIX = frozenset((".js", ".css", ".svg", ".png", ".ico", ".woff2"))

def unique(pairs):
    obj={}
    for k,v in pairs:
        if k in obj:
            raise RegistryRejected("DUPLICATE_BODY_KEY")
        obj[k]=v
    return obj

def ensure_test_env():
    if (os.environ.get("TARANOM_AI_OFFLINE_TEST_ONLY") != OFFLINE or
        os.environ.get("TARANOM_AI_HTTP_TEST_ONLY") != OFFLINE):
        raise RegistryRejected("AI_HTTP_ONLY_IN_EPHEMERAL_TESTS")

class Handler(BaseHTTPRequestHandler):
    registry: OfflineRegistry
    assets: Path

    def log_message(self, format, *args):
        # Synthetic-only, never log URL parameters or body.
        pass

    def reply(self, code, outcome, data=None):
        body=json.dumps({
            "contract":CONTRACT, "outcome":outcome, "syntheticOnly":True,
            "adminAuthenticated":False, "operatorAuthorized":False,
            "trainingAllowed":False, "inferenceSupported":False,
            "stageAllowed":False, "productionAllowed":False, "data":data
        },sort_keys=True).encode("utf-8")
        self.send_response(code)
        self.security_headers()
        self.send_header("Content-Type","application/json; charset=utf-8")
        self.send_header("Content-Length",str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def security_headers(self):
        self.send_header("Cache-Control","no-store")
        self.send_header("X-Content-Type-Options","nosniff")
        self.send_header("X-Frame-Options","DENY")
        self.send_header("Referrer-Policy","no-referrer")
        self.send_header("Content-Security-Policy",
            "default-src 'none'; script-src 'self'; style-src 'self'; "
            "connect-src 'self'; img-src 'self'; base-uri 'none'; "
            "form-action 'none'; object-src 'none'; frame-ancestors 'none'")

    def trusted(self):
        expected={f"127.0.0.1:{self.server.server_port}",
                  f"localhost:{self.server.server_port}"}
        if self.headers.get("Host") not in expected:
            return False
        if any(h in self.headers for h in ("Authorization","Cookie",
                "Forwarded","X-Forwarded-For","X-Forwarded-Host",
                "X-Forwarded-Proto","X-Tenant","X-Org","X-Actor")):
            return False
        origin=self.headers.get("Origin")
        if origin is not None and origin not in {
            f"http://{host}" for host in expected
        }:
            return False
        if self.headers.get("Transfer-Encoding") is not None:
            return False
        return True

    def do_OPTIONS(self):
        self.reply(403,"CROSS_ORIGIN_NOT_SUPPORTED")

    def do_GET(self):
        if not self.trusted():
            self.reply(403,"UNTRUSTED_REQUEST_AUTHORITY");return
        part=urlsplit(self.path)
        if part.path==BASE+"/health" and not part.query:
            self.reply(200,"SYNTHETIC_QA_ONLY",{"persistentDatabase":True})
            return
        if part.path==BASE+"/inventory":
            query=parse_qs(part.query,keep_blank_values=True,strict_parsing=True)
            if set(query)!={"tenant"} or len(query["tenant"])!=1:
                self.reply(400,"INVALID_TENANT_QUERY");return
            try:
                data=self.registry.read_inventory(query["tenant"][0])
            except RegistryRejected:
                self.reply(400,"INVALID_SYNTHETIC_TENANT");return
            except Exception:
                self.reply(503,"REGISTRY_UNAVAILABLE");return
            self.reply(200,"INVENTORY_READ",data)
            return
        if part.query:
            self.reply(404,"NOT_FOUND");return
        if part.path=="/operations/ai-lab":
            path=self.assets/"index.html"
            mime="text/html; charset=utf-8"
        elif part.path.startswith("/assets/"):
            name=unquote(part.path[len("/assets/"):])
            if not name or "/" in name or "\\" in name or name in {".",".."}:
                self.reply(404,"NOT_FOUND");return
            path=self.assets/"assets"/name
            if path.suffix not in ALLOWED_ASSET_SUFFIX:
                self.reply(404,"NOT_FOUND");return
            mime={".js":"application/javascript; charset=utf-8",
                  ".css":"text/css; charset=utf-8",".svg":"image/svg+xml",
                  ".png":"image/png",".ico":"image/x-icon",
                  ".woff2":"font/woff2"}[path.suffix]
        else:
            self.reply(404,"NOT_FOUND");return
        try:
            if path.is_symlink() or not path.resolve().is_relative_to(self.assets):
                raise OSError("UNTRUSTED_ASSET_PATH")
            content=path.read_bytes()
            if len(content)>10_000_000:raise OSError("ASSET_TOO_LARGE")
        except OSError:
            self.reply(503,"BUILD_ARTIFACT_UNAVAILABLE");return
        self.send_response(200)
        self.security_headers()
        self.send_header("Content-Type",mime)
        self.send_header("Content-Length",str(len(content)))
        self.end_headers()
        self.wfile.write(content)

    def do_POST(self):
        if not self.trusted():
            self.reply(403,"UNTRUSTED_REQUEST_AUTHORITY");return
        part=urlsplit(self.path)
        actions={BASE+"/datasets":self.registry.freeze_dataset,
                 BASE+"/models":self.registry.register_model,
                 BASE+"/evaluations":self.registry.evaluate_synthetic}
        if part.path not in actions or part.query:
            self.reply(404,"NOT_FOUND");return
        if self.headers.get("Content-Type")!="application/json":
            self.reply(415,"JSON_REQUIRED");return
        length=self.headers.get("Content-Length","")
        if not length.isascii() or not length.isdecimal() or length.startswith("0") or not 1<=int(length)<=MAX_BODY:
            self.reply(413,"INVALID_BODY_LENGTH");return
        try:
            body=json.loads(self.rfile.read(int(length)).decode("utf-8"),
                            object_pairs_hook=unique)
            data=actions[part.path](body)
        except RegistryRejected as exc:
            self.reply(409 if ("CONFLICT" in str(exc) or "ALREADY" in str(exc))
                       else 400,"REJECTED",{"reason":str(exc)})
            return
        except (ValueError,UnicodeError,TypeError):
            self.reply(400,"MALFORMED_REQUEST");return
        except Exception:
            self.reply(503,"REGISTRY_UNAVAILABLE");return
        self.reply(200,"RECORDED_SYNTHETIC_ONLY",data)

    def do_PUT(self):self.reply(405,"MUTATION_METHOD_DENIED")
    def do_PATCH(self):self.reply(405,"MUTATION_METHOD_DENIED")
    def do_DELETE(self):self.reply(405,"MUTATION_METHOD_DENIED")

def make_server(db: str,assets:Path,port=PORT):
    ensure_test_env()
    root=assets.resolve(strict=True)
    if not root.is_dir() or not (root/"index.html").is_file():
        raise RegistryRejected("COMPILED_WEB_ASSETS_REQUIRED")
    registry=OfflineRegistry(db)
    registry.initialize()
    class QaHandler(Handler):
        pass
    QaHandler.registry=registry
    QaHandler.assets=root
    return ThreadingHTTPServer(("127.0.0.1",port),QaHandler)

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--db",required=True)
    args=p.parse_args(argv)
    with make_server(args.db,ROOT/"apps/ops-web/dist") as server:
        server.serve_forever(poll_interval=0.25)

if __name__=="__main__":
    main()
