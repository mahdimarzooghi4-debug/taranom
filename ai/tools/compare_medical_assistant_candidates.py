"""Offline stdin/stdout runner for synthetic candidate interaction comparisons."""
from __future__ import annotations

import json
from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from ai.taranom_internal.medical_assistant_eval import (  # noqa: E402
    EvaluationRejected,
    compare_candidate_batches,
)


_MAX_INPUT_BYTES = 8 * 1024 * 1024


def _unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("DUPLICATE_JSON_KEY")
        result[key] = value
    return result


def _reject_constant(_: str) -> None:
    raise ValueError("NONSTANDARD_JSON_NUMBER")


def main() -> int:
    raw = sys.stdin.buffer.read(_MAX_INPUT_BYTES + 1)
    if len(raw) > _MAX_INPUT_BYTES:
        report = {"status": "REJECTED", "error": "INPUT_SIZE_LIMIT_EXCEEDED"}
        sys.stdout.write(json.dumps(report, sort_keys=True, separators=(",", ":")) + "\n")
        return 2
    try:
        payload = json.loads(
            raw.decode("utf-8"),
            object_pairs_hook=_unique_object,
            parse_constant=_reject_constant,
        )
        if type(payload) is not dict or set(payload) != {"cases", "candidate_outputs"}:
            raise EvaluationRejected("EXACT_COMPARISON_INPUT_SCHEMA_REQUIRED")
        report = compare_candidate_batches(payload["cases"], payload["candidate_outputs"])
    except (UnicodeDecodeError, json.JSONDecodeError, ValueError, EvaluationRejected, RecursionError):
        report = {"status": "REJECTED", "error": "INVALID_SYNTHETIC_COMPARISON_INPUT"}
        sys.stdout.write(json.dumps(report, sort_keys=True, separators=(",", ":")) + "\n")
        return 2
    sys.stdout.write(json.dumps(report, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
