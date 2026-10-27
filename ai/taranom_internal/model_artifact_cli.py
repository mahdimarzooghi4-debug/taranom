"""Offline, local-only Qwen artifact manifest verifier.

It reads a strict manifest and hashes declared local files. It never downloads,
loads, trains, or grants clinical or production authority to model weights.
"""
from __future__ import annotations

import argparse
import json
import sys

from .model_artifacts import ArtifactRejected, verify_local_model_artifact


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--artifact-root", required=True)
    arguments = parser.parse_args(argv)
    try:
        result = verify_local_model_artifact(
            arguments.manifest, arguments.artifact_root
        )
    except (ArtifactRejected, OSError, ValueError) as exc:
        result = {
            "status": "DENIED",
            "error": str(exc),
            "model_loaded": False,
            "network_used": False,
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
