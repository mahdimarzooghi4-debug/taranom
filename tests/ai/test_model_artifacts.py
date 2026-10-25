"""Synthetic filesystem tests for local model artifact preflight."""
import hashlib
import tempfile
import unittest
from pathlib import Path

from ai.taranom_internal.model_artifacts import ArtifactRejected, verify_local_model_manifest


def manifest(name="weights.part", data=b"synthetic-model-bytes"):
    return {
        "schema_version": 1,
        "candidate_ref": "QWEN_QWEN2_5_7B_INSTRUCT",
        "artifact_version": "snapshot-test-1",
        "files": [{"path": name, "sha256": hashlib.sha256(data).hexdigest(), "size_bytes": len(data)}],
    }


class LocalModelArtifactTests(unittest.TestCase):
    def test_verifies_local_bytes_without_loading_or_granting_authority(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "weights.part").write_bytes(b"synthetic-model-bytes")
            result = verify_local_model_manifest(manifest(), root)
        self.assertEqual("LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED", result["status"])
        self.assertFalse(result["model_loaded"])
        self.assertFalse(result["network_used"])
        self.assertFalse(result["license_reviewed"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["training_allowed"])
        self.assertFalse(result["production_allowed"])

    def test_rejects_path_traversal_remote_refs_and_modified_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "weights.part").write_bytes(b"different-bytes")
            for changed, error in (
                (manifest("../weights.part"), "SAFE_RELATIVE_ARTIFACT_PATH"),
                ({**manifest(), "candidate_ref": "https://huggingface.co/model"}, "UNAPPROVED_INTERNAL_CANDIDATE"),
                (manifest(), "ARTIFACT_SIZE_MISMATCH"),
            ):
                with self.subTest(error=error), self.assertRaisesRegex(ArtifactRejected, error):
                    verify_local_model_manifest(changed, root)
            manifest_with_equal_size_change = manifest()
            (root / "weights.part").write_bytes(b"x" * manifest_with_equal_size_change["files"][0]["size_bytes"])
            with self.assertRaisesRegex(ArtifactRejected, "ARTIFACT_SHA256_MISMATCH"):
                verify_local_model_manifest(manifest_with_equal_size_change, root)

    def test_rejects_symlinks_duplicate_paths_and_missing_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "real.part").write_bytes(b"synthetic-model-bytes")
            (root / "weights.part").symlink_to(root / "real.part")
            with self.assertRaisesRegex(ArtifactRejected, "SYMLINK_ARTIFACT_PATH"):
                verify_local_model_manifest(manifest(), root)
            duplicate = manifest()
            duplicate["files"] = duplicate["files"] * 2
            with self.assertRaisesRegex(ArtifactRejected, "DUPLICATE_ARTIFACT_PATH"):
                verify_local_model_manifest(duplicate, root)
            with self.assertRaisesRegex(ArtifactRejected, "LOCAL_ARTIFACT_FILE_MISSING"):
                verify_local_model_manifest(manifest("missing.part"), root)

    def test_rejects_invalid_digest_and_empty_manifest(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            bad = manifest()
            bad["files"][0]["sha256"] = "A" * 64
            with self.assertRaisesRegex(ArtifactRejected, "LOWERCASE_SHA256"):
                verify_local_model_manifest(bad, root)
            with self.assertRaisesRegex(ArtifactRejected, "BOUNDED_NONEMPTY_ARTIFACT"):
                verify_local_model_manifest({**manifest(), "files": []}, root)


if __name__ == "__main__":
    unittest.main()
