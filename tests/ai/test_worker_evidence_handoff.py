"""TRN-TECH-156: offline synthetic handoff, forgery and receiver isolation."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile,ZIP_STORED
from io import BytesIO

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/"tools"/"qa"))
import worker_evidence_handoff as handoff
import worker_evidence_anchor as anchor
from test_worker_evidence_anchor import chain


class HandoffTests(unittest.TestCase):
    def setUp(self):
        self.worker=Path(tempfile.mkdtemp(prefix="taranom-worker-",dir="/tmp"))
        self.holder=Path(tempfile.mkdtemp(prefix="taranom-anchor-",dir="/tmp"))
        self.export_dir=Path(tempfile.mkdtemp(prefix="taranom-handoff-",dir="/tmp"))
        self.receive_dir=Path(tempfile.mkdtemp(prefix="taranom-handoff-",dir="/tmp"))
        for path in (self.worker,self.holder,self.export_dir,self.receive_dir):
            self.addCleanup(lambda p=path: shutil.rmtree(p,ignore_errors=True))
        self.ledger=self.worker/"worker-evidence.jsonl"
        self.old="a"*32
        self.next="b"*32
        self.before=["Boot","Success","CapacityDenied"]
        self.after=["Boot","Success"]
        self.ledger.write_bytes(chain(self.before,[self.old]*3))
        self.flags=patch.dict(os.environ,{
            "TARANOM_CENTER_DEMO_TEST_ONLY":"CI_EPHEMERAL_ONLY",
            "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY":"CI_EPHEMERAL_ONLY",
            "TARANOM_AI_WORKER_EVIDENCE_FILE":str(self.ledger),
            "TARANOM_AI_WORKER_ANCHOR_DIR":str(self.holder),
            "TARANOM_AI_WORKER_HANDOFF_DIR":str(self.export_dir)
        })
        self.flags.start()
        self.addCleanup(self.flags.stop)
        anchor.create(self.holder)
        self.ledger.write_bytes(chain(self.before+self.after,
                                      [self.old]*3+[self.next]*2))
        self.assertEqual(5,handoff.export(self.export_dir,self.holder))
        shutil.copyfile(self.export_dir/handoff.ZIP_NAME,
                        self.receive_dir/handoff.ZIP_NAME)
        # shutil.copyfile creates destination with default umask; a
        # separate recipient must explicitly apply restrictive 0600,
        # and the verifier still rejects the permissive-path attack test.
        (self.receive_dir/handoff.ZIP_NAME).chmod(0o600)

    def consume(self,folder=None):
        return handoff.receive(folder or self.receive_dir,self.holder)

    def test_readonly_receiver_roundtrip_and_receipt_not_in_package(self):
        result=self.consume()
        self.assertEqual(5,result["recordsVerified"])
        self.assertEqual(2,result["processEpochs"])
        self.assertFalse(result["productionReady"])
        self.assertFalse(result["auditEvidence"])
        self.assertFalse(result["independentCustody"])
        data=(self.receive_dir/handoff.ZIP_NAME).read_bytes()
        ledger,manifest=handoff.open_zip(data)
        self.assertEqual(self.ledger.read_bytes(),ledger)
        self.assertEqual(2,len(handoff.decode_manifest(manifest).keys() &
                                {"bootCount","anchoredDigest"}))
        self.assertNotIn(b"custodian.key",data)
        self.assertNotIn(b"handoff-receipt.json",data)
        for forbidden in (b"requestRef",b"tenantRef",b"Bearer",
                          b"patient",b"TOP_SECRET_TEST_SENTINEL"):
            self.assertNotIn(forbidden,ledger)
        with self.assertRaises(FileExistsError):
            handoff.export(self.export_dir,self.holder)

    def test_modified_zip_byte_even_with_unchanged_internal_chain(self):
        archive=self.receive_dir/handoff.ZIP_NAME
        raw=bytearray(archive.read_bytes())
        raw[-24]^=0x01
        archive.write_bytes(raw)
        with self.assertRaisesRegex(ValueError,"ARCHIVE_REPLACED"):
            self.consume()

    def test_fully_rehashed_ledger_and_manifest_repacked(self):
        old=self.ledger.read_bytes()
        self.ledger.write_bytes(chain(
            ["Boot","ProcessRejected","CapacityDenied","Boot","Success"],
            [self.old]*3+[self.next]*2))
        new=self.ledger.read_bytes()
        self.assertNotEqual(old,new)
        data=(self.receive_dir/handoff.ZIP_NAME).read_bytes()
        _,raw_manifest=handoff.open_zip(data)
        manifest=json.loads(raw_manifest)
        final=json.loads(new.decode().splitlines()[-1])
        manifest["headDigest"]=final["digest"]
        manifest["ledgerSha256"]=handoff.sha(new)
        rewritten=handoff.stored_zip(new,handoff.anchor.canonical(manifest)+b"\n")
        (self.receive_dir/handoff.ZIP_NAME).write_bytes(rewritten)
        with self.assertRaisesRegex(ValueError,"ARCHIVE_REPLACED"):
            self.consume()

    def test_extra_member_and_reordered_zip_denied_without_receipt(self):
        raw=BytesIO()
        with ZipFile(raw,"w",compression=ZIP_STORED) as z:
            z.writestr("worker-evidence.jsonl",self.ledger.read_bytes())
            z.writestr("handoff.json",b"{}")
            z.writestr("../secret.txt",b"no")
        with self.assertRaisesRegex(ValueError,"ARCHIVE_CONTENT"):
            handoff.open_zip(raw.getvalue())
        raw2=BytesIO()
        with ZipFile(raw2,"w",compression=ZIP_STORED) as z:
            z.writestr("handoff.json",b"{}")
            z.writestr("worker-evidence.jsonl",self.ledger.read_bytes())
        with self.assertRaisesRegex(ValueError,"ARCHIVE_CONTENT"):
            handoff.open_zip(raw2.getvalue())

    def test_wrong_or_missing_receipt_and_key_fail_closed(self):
        receipt=self.holder/handoff.RECEIPT_NAME
        raw=receipt.read_bytes()
        malformed=json.loads(raw)
        malformed["auditEvidence"]=True
        receipt.write_bytes(handoff.anchor.canonical(malformed)+b"\n")
        with self.assertRaisesRegex(ValueError,"RECEIPT_INVALID"):
            self.consume()
        receipt.write_bytes(raw)
        key=self.holder/"custodian.key"
        prior=key.read_bytes()
        key.write_bytes(b"x"*32)
        with self.assertRaisesRegex(ValueError,"RECEIPT_MAC"):
            self.consume()
        key.write_bytes(prior)
        receipt.unlink()
        with self.assertRaises(FileNotFoundError):
            self.consume()

    def test_symlink_permissions_bad_paths_and_non_ci_denied(self):
        packed=self.receive_dir/handoff.ZIP_NAME
        packed.chmod(0o644)
        with self.assertRaisesRegex(ValueError,"FILE_PRIVILEGES"):
            self.consume()
        packed.chmod(0o600)
        other=self.receive_dir/"backup"
        packed.rename(other)
        packed.symlink_to(other)
        with self.assertRaisesRegex(ValueError,"FILE_PRIVILEGES"):
            self.consume()
        packed.unlink()
        other.rename(packed)
        with patch.dict(os.environ,{"TARANOM_CENTER_DEMO_TEST_ONLY":"OFF"}):
            with self.assertRaisesRegex(ValueError,"TEST_ONLY"):
                handoff.directory()
        with patch.dict(os.environ,{"TARANOM_AI_WORKER_HANDOFF_DIR":
                                    str(self.worker)}):
            with self.assertRaisesRegex(ValueError,"DIRECTORY"):
                handoff.directory()


if __name__=="__main__":
    unittest.main()
