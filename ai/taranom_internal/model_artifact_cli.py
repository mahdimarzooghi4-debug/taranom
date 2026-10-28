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
    verify_local_model_store_entry,
    list_local_model_store_entries,
)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest")
    parser.add_argument("--artifact-root")
    parser.add_argument("--expected-manifest-sha256")
    parser.add_argument("--expected-artifact-set-sha256")
    parser.add_argument("--store-root")
    parser.add_argument("--verify-store-ref")
    parser.add_argument("--list-store", action="store_true")
    arguments = parser.parse_args(argv)
    if arguments.verify_store_ref is not None or arguments.list_store:
        if (arguments.verify_store_ref is not None and arguments.list_store):
            parser.error("--verify-store-ref and --list-store are mutually exclusive")
        if (arguments.store_root is None or arguments.manifest is not None
                or arguments.artifact_root is not None
                or arguments.expected_manifest_sha256 is not None
                or arguments.expected_artifact_set_sha256 is not None):
            parser.error(
                "stored-package modes require --store-root and cannot combine "
                "with source manifest, artifact root, or expected pins"
            )
    elif arguments.manifest is None or arguments.artifact_root is None:
        parser.error("--manifest and --artifact-root are required for source verification")
    try:
        if arguments.list_store:
            result = list_local_model_store_entries(arguments.store_root)
        elif arguments.verify_store_ref is not None:
            result = verify_local_model_store_entry(
                arguments.store_root, arguments.verify_store_ref
            )
        elif arguments.store_root is None:
            result = verify_local_model_artifact(
                arguments.manifest,
                arguments.artifact_root,
                expected_manifest_sha256=arguments.expected_manifest_sha256,
                expected_artifact_set_sha256=arguments.expected_artifact_set_sha256,
            )
        else:
            result = store_verified_local_model_artifact(
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
