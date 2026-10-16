"""Real localhost HTTP and HTML console on isolated synthetic SQLite registry."""
import json
import os
from pathlib import Path
import tempfile
import threading
from urllib.request import Request,urlopen
from urllib.error import HTTPError
import unittest
from unittest.mock import patch

from ai.taranom_internal.http_qa import make_server
from test_offline_registry import dataset,model,evaluation

class QaApiTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(prefix="trn-ai-qa-")
        self.addCleanup(self.tmp.cleanup)
        self.env=patch.dict(os.environ,{"TARANOM_AI_OFFLINE_TEST_ONLY":"CI_EPHEMERAL_ONLY"})
        self.env.start();self.addCleanup(self.env.stop)
        self.server=make_server(str(Path(self.tmp.name)/"live.sqlite3"))
        self.base=f"http://127.0.0.1:{self.server.server_port}"
        worker=threading.Thread(target=self.server.serve_forever,daemon=True)
        worker.start()
        def stop():
            self.server.shutdown()
            self.server.server_close()
            worker.join(timeout=5)
        self.addCleanup(stop)

    def fetch(self,path,method="GET",payload=None,headers=None):
        body=(json.dumps(payload).encode("utf8") if payload is not None else None)
        h=headers if headers is not None else {}
        if method=="POST":
            h={"Origin":self.base,"Content-Type":"application/json",**h}
        req=Request(self.base+path,data=body,headers=h,method=method)
        try:
            with urlopen(req,timeout=5) as res:
                return res.status,res.read(),dict(res.headers)
        except HTTPError as exc:
            return exc.code,exc.read(),dict(exc.headers)
    def post(self,path,claim,headers=None):
        code,body,_=self.fetch(path,"POST",claim,headers)
        return code,json.loads(body)

    def test_full_http_dataset_model_evaluation_inventory(self):
        base="/internal/qa/v1/ai"
        for path,claim in (("/datasets",dataset()),("/models",model()),
                           ("/evaluations",evaluation())):
            code,data=self.post(base+path,claim)
            self.assertEqual(200,code,data)
            self.assertFalse(data["productionAllowed"])
            self.assertFalse(data["adminAuthorized"])
            self.assertFalse(data["modelActivated"])
        code,raw,_=self.fetch(base+"/inventory?tenant=test:tenant-a")
        self.assertEqual(200,code)
        record=json.loads(raw)
        self.assertEqual(1,len(record["data"]["model_versions"]))
        self.assertEqual(1,len(record["data"]["evaluations"]))
        self.assertEqual("0.25",record["data"]["evaluations"][0]["mse"])
        self.assertFalse(record["data"]["training_allowed"])

    def test_idempotent_http_retry_keeps_one_dataset(self):
        base="/internal/qa/v1/ai"
        a,b=self.post(base+"/datasets",dataset())
        self.assertEqual(200,a)
        a,b=self.post(base+"/datasets",dataset())
        self.assertEqual("REPLAY_UNVERIFIED",b["data"]["status"])
        _,raw,_=self.fetch(base+"/inventory?tenant=test:tenant-a")
        self.assertEqual(1,len(json.loads(raw)["data"]["datasets"]))

    def test_tampered_model_replay_conflicts(self):
        base="/internal/qa/v1/ai"
        self.post(base+"/datasets",dataset())
        self.post(base+"/models",model())
        m=model()
        m["artifact_sha256"]="C"*64
        status,data=self.post(base+"/models",m)
        self.assertEqual(409,status)
        self.assertFalse(data["productionAllowed"])

    def test_reject_missing_origin_and_cross_site(self):
        path="/internal/qa/v1/ai/datasets"
        for origin in ("https://evil.example","http://localhost:4377"):
            code,data=self.post(path,dataset(),{"Origin":origin})
            self.assertEqual(403,code)
        code,data=self.post(path,dataset(),{"Origin":""})
        self.assertEqual(403,code)

    def test_reject_auth_cookies_and_tenant_headers(self):
        path="/internal/qa/v1/ai/inventory?tenant=test:tenant-a"
        for head in ({"Cookie":"fake=1"},{"Authorization":"Bearer fake"},
                     {"X-Tenant":"test:other"},{"Forwarded":"host=evil"}):
            code,raw,_=self.fetch(path,headers=head)
            self.assertEqual(403,code,raw)

    def test_no_cross_origin_post_or_cors_grant(self):
        code,raw,headers=self.fetch("/internal/qa/v1/ai/inventory?tenant=test:tenant-a")
        self.assertEqual(200,code)
        self.assertEqual("null",headers.get("Access-Control-Allow-Origin"))
        self.assertIn("default-src 'none'",headers.get("Content-Security-Policy",""))

    def test_qa_html_is_real_local_panel_with_no_activation(self):
        code,html,_=self.fetch("/ai-qa")
        self.assertEqual(200,code)
        self.assertIn(b'<html lang="fa" dir="rtl">',html)
        self.assertIn(b'/ai-qa/app.js',html)
        self.assertNotIn(b"<script>window",html)
        code,script,_=self.fetch("/ai-qa/app.js")
        self.assertEqual(200,code)
        self.assertIn(b"fetch(",script)
        self.assertIn(b"adminAuthorized",script)
        self.assertNotIn(b"innerHTML",script)

    def test_query_host_and_extra_path_fail_closed(self):
        for path in ("/internal/qa/v1/ai/inventory?tenant=test:tenant-a&role=ADMIN",
                     "/internal/qa/v1/ai/inventory?tenant=test:tenant-a&tenant=test:other"):
            code,_,_=self.fetch(path)
            self.assertEqual(400,code)
        code,_,_=self.fetch("/internal/qa/v1/ai/promote")
        self.assertEqual(404,code)

    def test_cross_tenant_access_returns_only_own_metadata(self):
        self.post("/internal/qa/v1/ai/datasets",dataset())
        code,raw,_=self.fetch("/internal/qa/v1/ai/inventory?tenant=test:other")
        self.assertEqual(200,code)
        self.assertEqual([],json.loads(raw)["data"]["datasets"])

    def test_malformed_json_and_wrong_content_type_denied(self):
        self.assertEqual(400,self.post("/internal/qa/v1/ai/datasets",{"broken":1})[0])
        code,raw,_=self.fetch("/internal/qa/v1/ai/models","POST",model(),
                               {"Content-Type":"text/plain"})
        self.assertEqual(415,code)

    def test_missing_test_flag_prevents_server_start(self):
        with patch.dict(os.environ,{"TARANOM_AI_OFFLINE_TEST_ONLY":""}):
            with self.assertRaises(Exception):
                make_server(str(Path(self.tmp.name)/"other.sqlite3"))

    def test_outside_data_denied_and_no_training_route(self):
        code,payload=self.post("/internal/qa/v1/ai/training",dataset())
        self.assertEqual(404,code)
        self.assertFalse(payload["productionAllowed"])

if __name__=="__main__":unittest.main()
