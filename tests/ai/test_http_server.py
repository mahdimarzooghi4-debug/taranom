"""Real loopback HTTP source-to-SQLite tests for the AI QA control plane."""
import http.client
import json
import os
from pathlib import Path
from threading import Thread
import tempfile
import unittest
from unittest.mock import patch

from ai.taranom_internal.server import make_server, CONTRACT
from tests.ai.test_offline_registry import dataset, model, evaluation

class HttpQaTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.patch=patch.dict(os.environ,{
            "TARANOM_AI_OFFLINE_TEST_ONLY":"CI_EPHEMERAL_ONLY",
            "TARANOM_AI_HTTP_TEST_ONLY":"CI_EPHEMERAL_ONLY"})
        self.patch.start();self.addCleanup(self.patch.stop)
        self.assets=Path(self.tmp.name)/"dist"
        self.assets.mkdir()
        (self.assets/"index.html").write_text("<html>test</html>",encoding="utf-8")
        self.db=str(Path(self.tmp.name)/"registry.sqlite3")
        self.server=make_server(self.db,self.assets,0)
        self.thread=Thread(target=self.server.serve_forever,daemon=True)
        self.thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.addCleanup(self.thread.join,2)
    def request(self,method,path,body=None,headers=None):
        c=http.client.HTTPConnection("127.0.0.1",self.server.server_port,timeout=5)
        h={"Host":f"127.0.0.1:{self.server.server_port}"}
        if headers:h.update(headers)
        if body is not None:
            h["Content-Type"]="application/json"
            payload=json.dumps(body)
        else:payload=None
        c.request(method,path,payload,headers=h)
        res=c.getresponse()
        raw=res.read()
        return res.status,res.headers,json.loads(raw) if raw.startswith(b"{") else raw
    def test_http_full_persistent_dataset_model_evaluation(self):
        for path,payload in (("/datasets",dataset()),("/models",model()),("/evaluations",evaluation())):
            status,headers,v=self.request("POST","/internal/ai-test/v1"+path,payload)
            self.assertEqual(200,status,v)
            self.assertEqual(CONTRACT,v["contract"])
            self.assertFalse(v["productionAllowed"])
            self.assertFalse(v["adminAuthenticated"])
            self.assertEqual("no-store",headers["Cache-Control"])
        s,h,r=self.request("GET","/internal/ai-test/v1/inventory?tenant=test:tenant-a")
        self.assertEqual(200,s)
        self.assertEqual(1,len(r["data"]["datasets"]))
        self.assertEqual(1,len(r["data"]["model_versions"]))
        self.assertEqual(1,len(r["data"]["evaluations"]))
        self.assertEqual("0.25",r["data"]["evaluations"][0]["mse"])
    def test_no_auth_cookie_forwarded_cross_origin(self):
        for header in ({"Authorization":"Bearer forged"},{"Cookie":"fake=1"},
                       {"X-Forwarded-Host":"evil"},{"Origin":"https://evil.example"},
                       {"X-Tenant":"test:other"},{"Host":"evil.example"}):
            status,_,body=self.request("GET","/internal/ai-test/v1/health",headers=header)
            self.assertEqual(403,status,(header,body))
    def test_missing_test_mode_denied(self):
        with patch.dict(os.environ,{"TARANOM_AI_HTTP_TEST_ONLY":"OFF"}):
            with self.assertRaisesRegex(Exception,"EPHEMERAL"):
                make_server(self.db,self.assets,0)
    def test_unknown_path_or_post_no_override(self):
        self.assertEqual(404,self.request("POST","/internal/ai-test/v1/activate",{"productionAllowed":True})[0])
        self.assertEqual(405,self.request("DELETE","/internal/ai-test/v1/models")[0])
        self.assertEqual(403,self.request("OPTIONS","/internal/ai-test/v1/models")[0])
    def test_invalid_body_and_unknown_authority_denied(self):
        bad=dataset()
        bad["authorizeProduction"]=True
        s,_,result=self.request("POST","/internal/ai-test/v1/datasets",bad)
        self.assertEqual(400,s)
        self.assertEqual("REJECTED",result["outcome"])
        self.assertFalse(result["operatorAuthorized"])
    def test_replay_and_change_denied(self):
        self.assertEqual(200,self.request("POST","/internal/ai-test/v1/datasets",dataset())[0])
        changed=dataset();changed["sources"][0]["source_sha256"]="C"*64
        s,_,body=self.request("POST","/internal/ai-test/v1/datasets",changed)
        self.assertEqual(409,s)
        self.assertEqual("REJECTED",body["outcome"])
    def test_tenant_filter_rejects_malformed_and_multiple(self):
        for query in ("","?tenant=person:real","?tenant=test:a&tenant=test:b"):
            status,_,_=self.request("GET","/internal/ai-test/v1/inventory"+query)
            self.assertEqual(400,status)
    def test_static_index_no_private_files(self):
        status,headers,body=self.request("GET","/operations/ai-lab")
        self.assertEqual(200,status)
        self.assertIn(b"<html>",body)
        self.assertEqual("no-store",headers["Cache-Control"])
        self.assertEqual(404,self.request("GET","/assets/../../etc/passwd")[0])
    def test_no_remote_binding_and_no_response_cors(self):
        self.assertEqual("127.0.0.1",self.server.server_address[0])
        status,headers,body=self.request("GET","/internal/ai-test/v1/health")
        self.assertEqual(200,status)
        self.assertIsNone(headers.get("Access-Control-Allow-Origin"))
        self.assertFalse(body["productionAllowed"])

if __name__=="__main__":unittest.main()
