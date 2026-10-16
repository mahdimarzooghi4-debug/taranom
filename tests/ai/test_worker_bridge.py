"""TRN-TECH-149: real one-shot signed child Python process, negative protocol cases."""
import hmac
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from ai.taranom_internal.registry import OfflineRegistry

KEY = "8f" * 32
CONTRACT = "TRN-TECH-149"
CMD = "READ_SYNTHETIC_INVENTORY"


def sign(*fields):
    return hmac.new(bytes.fromhex(KEY),
                    "\n".join(map(str, fields)).encode(), hashlib.sha256).hexdigest().upper()


def frame():
    r = {"contract": CONTRACT, "command": CMD, "requestRef": "test:probe-1",
         "tenantRef": "test:team-1", "nonce": "a" * 32}
    r["mac"] = sign(CONTRACT, CMD, r["requestRef"], r["tenantRef"], r["nonce"])
    return r


class IsolatedWorkerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="taranom-worker-")
        self.addCleanup(self.temp.cleanup)
        self.db = str(Path(self.temp.name) / "registry.sqlite3")
        self.env = {"PYTHONPATH": str(Path(__file__).resolve().parents[2]),
                    "TARANOM_AI_OFFLINE_TEST_ONLY": "CI_EPHEMERAL_ONLY",
                    "TARANOM_AI_WORKER_QA": "CI_EPHEMERAL_ONLY",
                    "TARANOM_AI_BRIDGE_TEST_DB": self.db,
                    "TARANOM_AI_WORKER_BRIDGE_TEST_KEY": KEY}
        with unittest.mock.patch.dict(os.environ, self.env):
            OfflineRegistry(self.db).initialize()

    def run_worker(self, packet, env=None):
        p = subprocess.run([sys.executable, "-B", "-m",
                            "ai.taranom_internal.bridge_worker"],
                           input=packet, text=True, capture_output=True,
                           env=self.env if env is None else env, timeout=6)
        return p

    def test_valid_signed_envelope_returns_real_read_only_sqlite_counts(self):
        p = self.run_worker(json.dumps(frame()))
        self.assertEqual(0, p.returncode, p.stderr)
        v = json.loads(p.stdout)
        self.assertEqual(9, len(v))
        self.assertEqual({"datasets": 0, "modelVersions": 0, "evaluations": 0,
                          "trainingRuns": 0, "benchmarks": 0}, v["counts"])
        for denied in ("adminAuthenticated", "productionAllowed", "trainingAllowed"):
            self.assertIs(v[denied], False)
        self.assertEqual(sign(v["requestRef"], v["nonce"], v["status"],
                              0, 0, 0, 0, 0), v["mac"])
        self.assertNotIn(KEY, p.stdout)

    def test_invalid_auth_and_bypass_fields_fail_without_echo(self):
        for mutation in (
            {"mac": "0" * 64}, {"tenantRef": "real:patient"},
            {"command": "AUTHORIZE_PRODUCTION"}, {"productionAllowed": True},
            {"nonce": "bad"}, {"requestRef": "test:bad\nline"},
            {"contract": "TRN-TECH-142"},
        ):
            payload = {**frame(), **mutation}
            p = self.run_worker(json.dumps(payload))
            self.assertEqual(17, p.returncode, mutation)
            self.assertEqual("", p.stdout)
        p = self.run_worker(json.dumps(frame())[:-1] + ',"nonce":"b"}')
        self.assertEqual(17, p.returncode)
        self.assertEqual("", p.stdout)

    def test_missing_flags_wrong_secret_overlong_body_and_path(self):
        for env in (
            {k: v for k, v in self.env.items() if k != "TARANOM_AI_WORKER_QA"},
            {**self.env, "TARANOM_AI_WORKER_BRIDGE_TEST_KEY": "0" * 64},
            {**self.env, "TARANOM_AI_BRIDGE_TEST_DB": "/etc/passwd"},
        ):
            p = self.run_worker(json.dumps(frame()), env)
            self.assertEqual(17, p.returncode)
            self.assertEqual("", p.stdout)
        p = self.run_worker(json.dumps(frame()) + " " * 1200)
        self.assertEqual(17, p.returncode)
        self.assertEqual("", p.stdout)


if __name__ == "__main__":
    unittest.main()
