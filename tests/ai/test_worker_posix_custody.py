"""TRN-TECH-157: no sudo invocation outside strictly gated CI."""
from __future__ import annotations
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/"tools"/"qa"))
import worker_posix_custody_ci as boundary


class PosixCustodyContracts(unittest.TestCase):
    def test_refuses_ungated_execution_without_shell(self):
        with patch.dict(os.environ,{
            "TARANOM_CENTER_DEMO_TEST_ONLY":"OFF",
            "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY":"OFF",
            "CI":"false"
        }):
            with self.assertRaises(ValueError):
                boundary.guard()

    def test_refuses_root_execution(self):
        with patch.object(boundary.os,"geteuid",return_value=0):
            with patch.dict(os.environ,{
                "TARANOM_CENTER_DEMO_TEST_ONLY":"CI_EPHEMERAL_ONLY",
                "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY":"CI_EPHEMERAL_ONLY",
                "CI":"true"
            }):
                with self.assertRaisesRegex(ValueError,"RUNNER_MUST_BE_NONROOT"):
                    boundary.guard()

    def test_rejects_dir_symlink_wrong_root_and_weak_mode(self):
        temp=Path(tempfile.mkdtemp(prefix="taranom-anchor-",dir="/tmp"))
        self.addCleanup(lambda: shutil.rmtree(temp,ignore_errors=True))
        with patch.dict(os.environ,{"TARANOM_QA_CUSTODY_DIR":str(temp)}):
            self.assertEqual(temp,boundary.folder("TARANOM_QA_CUSTODY_DIR","anchor"))
            temp.chmod(0o755)
            with self.assertRaisesRegex(ValueError,"FOLDER_MODE"):
                boundary.folder("TARANOM_QA_CUSTODY_DIR","anchor")
            temp.chmod(0o700)
        bad=Path(tempfile.mkdtemp(prefix="bad-",dir="/tmp"))
        self.addCleanup(lambda: shutil.rmtree(bad,ignore_errors=True))
        with patch.dict(os.environ,{"TARANOM_QA_CUSTODY_DIR":str(bad)}):
            with self.assertRaisesRegex(ValueError,"FOLDER_SCOPE"):
                boundary.folder("TARANOM_QA_CUSTODY_DIR","anchor")

    def test_source_path_rejects_non_synthetic_and_symlink(self):
        with patch.dict(os.environ,{
            "TARANOM_AI_WORKER_EVIDENCE_FILE":"/etc/shadow"}):
            with self.assertRaisesRegex(ValueError,"ORIGINAL_SCOPE"):
                boundary.original()
        source=Path(tempfile.mkdtemp(prefix="taranom-worker-",dir="/tmp"))
        self.addCleanup(lambda: shutil.rmtree(source,ignore_errors=True))
        outside=source/"data"
        outside.write_bytes(b"synthetic")
        link=source/"worker-evidence.jsonl"
        link.symlink_to(outside)
        with patch.dict(os.environ,{
            "TARANOM_AI_WORKER_EVIDENCE_FILE":str(link)}):
            with self.assertRaisesRegex(ValueError,"ORIGINAL_SCOPE"):
                boundary.original()


if __name__=="__main__":
    unittest.main()
