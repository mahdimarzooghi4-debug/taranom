"""TRN-TECH-086 recovery admission and fail-closed snapshot rules; no local Docker."""
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT / "tools/recovery"))
import rehearse_ephemeral_postgres as rec


class EphemeralRecoveryContractTests(unittest.TestCase):
    def setUp(self):
        self.valid = {
            "GITHUB_ACTIONS": "true",
            "CI": "true",
            "TARANOM_CENTER_DEMO_TEST_ONLY": "CI_EPHEMERAL_ONLY",
            "TARANOM_TEST_PG": rec.EXPECTED_CONNECTION
        }

    def test_refuse_all_non_ci_or_other_database_targets(self):
        self.assertTrue(rec.environment_allowed(self.valid))
        for key,value in (
            ("CI","false"),("GITHUB_ACTIONS","false"),
            ("TARANOM_CENTER_DEMO_TEST_ONLY","PRODUCTION"),
            ("TARANOM_TEST_PG",rec.EXPECTED_CONNECTION.replace(
                "taranom_sprint64_center","production")),
            ("TARANOM_TEST_PG",rec.EXPECTED_CONNECTION.replace(
                "localhost","external.server.example")),
        ):
            with self.subTest(key=key,value=value):
                self.assertFalse(rec.environment_allowed({**self.valid,key:value}))
        self.assertFalse(rec.environment_allowed({}))

    def test_rehearsal_does_not_invoke_docker_if_not_ephemeral(self):
        with patch.dict(rec.os.environ,{},clear=True),\
             patch.object(rec, "_docker_container",side_effect=AssertionError):
            with self.assertRaisesRegex(rec.RecoveryError,
                                        "EPHEMERAL_TEST_ENVIRONMENT_REQUIRED"):
                rec.rehearse()

    def test_reject_real_or_unexpected_tables_and_missing_tables(self):
        sample=list(rec.MANDATORY_TABLES)
        self.assertEqual(set(rec.validate_tables(sample)),set(sample))
        for bad in (sample[:-1],sample+["patients"],sample+["public.real"],
                    sample+["internal_test_bad;DROP"],sample+sample):
            with self.subTest(bad=bad):
                with self.assertRaises(rec.RecoveryError):
                    rec.validate_tables(bad)

    def test_semantic_row_digest_order_is_stable_but_content_aware(self):
        a=b'{"id":1,"actor":"test:one"}\n{"id":2,"ok":false}\n'
        b=b'{"ok":false,"id":2}\n{"actor":"test:one","id":1}\n'
        self.assertEqual(rec.canonical_rows(a),rec.canonical_rows(b))
        self.assertNotEqual(rec.canonical_rows(a),
                            rec.canonical_rows(b'{"id":2,"ok":true}\n'))

    def test_nested_event_command_json_survives_raw_json_line(self):
        row = json.dumps({
            "case_ref": "test:case",
            "command_json": json.dumps({"CaseRef": "test:case",
                                        "Extra": "test:quoted\\\"x\\\""}),
            "revision": 1
        }).encode()
        count, digest = rec.canonical_rows(row + b"\n")
        self.assertEqual(count, 1)
        self.assertEqual(len(digest), 64)
        # COPY text format escapes JSON backslashes again and is *not*
        # acceptable as a source for json.loads on PostgreSQL output.
        self.assertIn(b"\\\\", row)

    def test_invalid_rows_do_not_create_false_recovery_evidence(self):
        for value in (b"{bad", b"\xff", b'x'*(rec.MAX_QUERY_SIZE+1)):
            with self.assertRaises(rec.RecoveryError):
                rec.canonical_rows(value)

    def test_no_shell_and_no_success_from_unexpected_docker_failure(self):
        class Failure: returncode=1;stdout=b""
        with patch.object(rec.subprocess,"run",return_value=Failure()) as proc:
            with self.assertRaisesRegex(rec.RecoveryError,
                                        "RECOVERY_OPERATION_FAILED"):
                rec.run_checked(["docker","ps"])
        self.assertFalse(proc.call_args.kwargs.get("shell",False))

    def test_append_only_guard_must_reject_modification(self):
        class FalseSuccess: returncode=0;stdout=b""
        with patch.object(rec.subprocess,"run",return_value=FalseSuccess()):
            with self.assertRaisesRegex(rec.RecoveryError,
                                        "RESTORED_APPEND_ONLY_GUARD_NOT_ENFORCED"):
                rec.run_checked(["docker","exec","safe"],expect_failure=True)

    def test_recovery_safety_contract_is_non_authorizing(self):
        source=(ROOT/"tools/recovery/rehearse_ephemeral_postgres.py").read_text()
        self.assertIn('"production_recovery_verified": False',source)
        self.assertIn('"national_release_allowed": False',source)
        self.assertIn('"rpo_rto_verified": False',source)


if __name__=="__main__":
    unittest.main()
