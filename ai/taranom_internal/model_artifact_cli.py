"""Offline, local-only Qwen artifact manifest verifier.

It reads a strict manifest and hashes declared local files. It never downloads,
loads, trains, or grants clinical or production authority to model weights.
"""
from __future__ import annotations

import argparse
import json
import sys

from .model_artifacts import (
    ArtifactRejected,
    store_verified_local_model_artifact,
    verify_local_model_artifact,
)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--artifact-root", required=True)
    parser.add_argument("--expected-manifest-sha256")
    parser.add_argument("--expected-artifact-set-sha256")
    parser.add_argument("--store-root")
    arguments = parser.parse_args(argv)
    try:
        verifier = (
            store_verified_local_model_artifact
            if arguments.store_root is not None else verify_local_model_artifact
        )
        if arguments.store_root is None:
            result = verifier(
                arguments.manifest,
                arguments.artifact_root,
                expected_manifest_sha256=arguments.expected_manifest_sha256,
                expected_artifact_set_sha256=arguments.expected_artifact_set_sha256,
            )
        else:
            result = verifier(
                arguments.manifest,
                arguments.artifact_root,
                arguments.store_root,
                expected_manifest_sha256=arguments.expected_manifest_sha256,
                expected_artifact_set_sha256=arguments.expected_artifact_set_sha256,
            )
    except (ArtifactRejected, OSError, ValueError) as exc:
        result = {
            "status": "DENIED",
            "error": str(exc),
            "model_loaded": False,
            "network_used": False,
            "manifest_sha256": None,
            "artifact_set_sha256": None,
            "store_ref": None,
            "store_created": False,
            "manifest_pin_supplied": arguments.expected_manifest_sha256 is not None,
            "artifact_set_pin_supplied": arguments.expected_artifact_set_sha256 is not None,
            "manifest_authenticity_verified": False,
            "license_reviewed": False,
            "clinical_use_allowed": False,
            "training_allowed": False,
            "production_allowed": False,
        }
        print(json.dumps(result, sort_keys=True))
        return 2

    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
