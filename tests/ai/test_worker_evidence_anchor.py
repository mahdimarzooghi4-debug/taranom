"""TRN-TECH-155: independent anchored prefix defeats plain SHA rehash."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "qa"))
import worker_evidence_anchor as anchor


def chain(kinds, epochs):
    out=[]
    prev="0"*64
    for i,(kind,epoch) in enumerate(zip(kinds,epochs),1):
        digest=hashlib.sha256(
            ("TRN-TECH-154\n"+str(i)+"\n"+epoch+"\n"+kind+"\n"+prev
             ).encode()).hexdigest().upper()
        obj={"sequence":i,"epoch":epoch,"kind":kind,
             "previousDigest":prev,"digest":digest}
        out.append(json.dumps(obj,separators=(",",":")))
        prev=digest
    return ("\n".join(out)+"\n").encode()


class AnchorProofTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        # Existing test ledger path is strictly constrained under /tmp.
        self.worker=Path(tempfile.mkdtemp(prefix="taranom-worker-",dir="/tmp"))
        self.custody=Path(tempfile.mkdtemp(prefix="taranom-anchor-",dir="/tmp"))
        self.addCleanup(lambda: __import__("shutil").rmtree(self.custody))
        self.addCleanup(lambda: __import__("shutil").rmtree(self.worker))
        self.ledger=self.worker/"worker-evidence.jsonl"
        self.old="a"*32
        self.new="b"*32
        self.before=["Boot","Success","CapacityDenied"]
        self.after=["Boot","Success"]
        self.write(self.before,[self.old]*3)
        self.flags=patch.dict(os.environ,{
            "TARANOM_CENTER_DEMO_TEST_ONLY":"CI_EPHEMERAL_ONLY",
            "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY":"CI_EPHEMERAL_ONLY",
            "TARANOM_AI_WORKER_EVIDENCE_FILE":str(self.ledger),
            "TARANOM_AI_WORKER_ANCHOR_DIR":str(self.custody),
        })
        self.flags.start()
        self.addCleanup(self.flags.stop)

    def write(self,kinds,epochs):
        self.ledger.write_bytes(chain(kinds,epochs))

    def extend(self):
        self.write(self.before+self.after,[self.old]*3+[self.new]*2)

    def test_anchor_roundtrip_and_replayed_creation_denied(self):
        data=anchor.create(self.custody)
        self.assertEqual(3,data["anchoredCount"])
        self.assertEqual(self.old,data["anchoredEpoch"])
        self.assertEqual(0o600,stat.S_IMODE(
            (self.custody/"custodian.key").stat().st_mode))
        self.assertEqual(0o600,stat.S_IMODE(
            (self.custody/"anchor.json").stat().st_mode))
        with self.assertRaises(FileExistsError):
            anchor.create(self.custody)
        self.extend()
        verified=anchor.verify_continuation(self.custody)
        self.assertTrue(verified["newEpochValidated"])
        self.assertFalse(verified["auditEvidence"])
        self.assertFalse(verified["custodyVerified"])
        self.assertFalse(verified["productionReady"])

    def test_full_history_rehash_is_not_trusted(self):
        anchor.create(self.custody)
        self.extend()
        self.assertTrue(anchor.verify_continuation(self.custody))
        # Fully rewrite every public SHA-256 link, so the standalone
        # ledger verifier still accepts the replacement history.
        changed=["Boot","ProcessRejected","CapacityDenied","Boot","Success"]
        epochs=[self.old]*3+[self.new]*2
        self.write(changed,epochs)
        rows,boots,last=anchor.read_records()
        self.assertEqual(5,len(rows))
        with self.assertRaisesRegex(ValueError,"PREFIX_OR_RESTART_CHANGED"):
            anchor.verify_continuation(self.custody)

    def test_truncation_and_missing_recovery(self):
        anchor.create(self.custody)
        # Still a completely valid internal hash chain of old three rows.
        anchor.read_records()
        with self.assertRaisesRegex(ValueError,"PREFIX_OR_RESTART_CHANGED"):
            anchor.verify_continuation(self.custody)
        self.extend()
        self.ledger.write_bytes(chain(
            self.before+["Boot"],[self.old]*3+[self.new]))
        with self.assertRaisesRegex(ValueError,"PREFIX_OR_RESTART_CHANGED"):
            anchor.verify_continuation(self.custody)

    def test_bad_anchor_mac_wrong_key_and_manifest_fields(self):
        anchor.create(self.custody)
        self.extend()
        path=self.custody/"anchor.json"
        original=path.read_bytes()
        manifest=json.loads(original)
        manifest["anchoredCount"]=4
        path.write_bytes(anchor.canonical(manifest)+b"\n")
        with self.assertRaisesRegex(ValueError,"BAD_MAC"):
            anchor.verify_continuation(self.custody)
        path.write_bytes(original)
        key=self.custody/"custodian.key"
        key.write_bytes(b"x"*32)
        with self.assertRaisesRegex(ValueError,"BAD_MAC"):
            anchor.verify_continuation(self.custody)
        key.unlink()
        with self.assertRaises(FileNotFoundError):
            anchor.verify_continuation(self.custody)

    def test_symlink_permissions_and_wrong_epoch_denied(self):
        anchor.create(self.custody)
        self.extend()
        key=self.custody/"custodian.key"
        key.chmod(0o644)
        with self.assertRaisesRegex(ValueError,"FILE_PRIVILEGES"):
            anchor.verify_continuation(self.custody)
        key.chmod(0o600)
        manifest=self.custody/"anchor.json"
        backup=self.custody/"saved-anchor"
        manifest.rename(backup)
        manifest.symlink_to(backup)
        with self.assertRaisesRegex(ValueError,"FILE_PRIVILEGES"):
            anchor.verify_continuation(self.custody)
        manifest.unlink()
        backup.rename(manifest)
        self.write(self.before+self.after,[self.old]*3+["c"*32]*2)
        with self.assertRaisesRegex(ValueError,"PREFIX_OR_RESTART_CHANGED"):
            anchor.verify_continuation(self.custody)

    def test_disallow_non_ci_or_invalid_anchor_directory(self):
        with patch.dict(os.environ,{"TARANOM_CENTER_DEMO_TEST_ONLY":"OFF"}):
            with self.assertRaisesRegex(ValueError,"TEST_ONLY"):
                anchor.directory()
        with patch.dict(os.environ,{"TARANOM_AI_WORKER_ANCHOR_DIR":str(self.worker)}):
            with self.assertRaisesRegex(ValueError,"DIRECTORY"):
                anchor.directory()


if __name__=="__main__":
    unittest.main()
