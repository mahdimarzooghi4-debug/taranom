"""TRN-TECH-149: one-shot isolated .NET -> Python synthetic inventory worker.

No HTTP listener, external API, data ingestion, model invocation or decision.
Only caller-spawned local process with short-lived CI-only HMAC challenge.
"""
from __future__ import annotations

import hashlib
import hmac
import json
import os
import re
import sys

from .registry import OfflineRegistry, RegistryRejected

CONTRACT = "TRN-TECH-149"
COMMAND = "READ_SYNTHETIC_INVENTORY"
REF = re.compile(r"test:[A-Za-z0-9_:-]{1,123}\Z")
NONCE = re.compile(r"[0-9a-f]{32}\Z")
MAC = re.compile(r"[0-9A-F]{64}\Z")
FIELDS = ("datasets", "modelVersions", "evaluations", "trainingRuns", "benchmarks")


def unique(pairs):
    result = {}
    for name, value in pairs:
        if name in result:
            raise ValueError("DUPLICATE_FIELD")
        result[name] = value
    return result


def signed(key, *fields):
    return hmac.new(key, "\n".join(map(str, fields)).encode("utf-8"),
                    hashlib.sha256).hexdigest().upper()


def execute(frame, key, db):
    if not isinstance(frame, dict) or set(frame) != {
        "contract", "command", "requestRef", "tenantRef", "nonce", "mac"
    }:
        raise ValueError("UNEXPECTED_FIELDS")
    request = frame["requestRef"]
    tenant = frame["tenantRef"]
    nonce = frame["nonce"]
    mac = frame["mac"]
    if (frame["contract"] != CONTRACT or frame["command"] != COMMAND or
        not isinstance(request, str) or not REF.fullmatch(request) or
        not isinstance(tenant, str) or not REF.fullmatch(tenant) or
        not isinstance(nonce, str) or not NONCE.fullmatch(nonce) or
        not isinstance(mac, str) or not MAC.fullmatch(mac)):
        raise ValueError("INVALID_FRAME")
    expected = signed(key, CONTRACT, COMMAND, request, tenant, nonce)
    if not hmac.compare_digest(mac, expected):
        raise ValueError("BAD_CHALLENGE")
    inventory = OfflineRegistry(db).read_inventory(tenant)
    if (inventory.get("production_allowed") is not False or
        inventory.get("training_allowed") is not False):
        raise ValueError("UNAUTHORIZED_REGISTRY_STATE")
    counts = {
        "datasets": len(inventory["datasets"]),
        "modelVersions": len(inventory["model_versions"]),
        "evaluations": len(inventory["evaluations"]),
        "trainingRuns": len(inventory["training_runs"]),
        "benchmarks": len(inventory["shadow_benchmarks"]),
    }
    if any(n > 4096 for n in counts.values()):
        raise ValueError("QA_INVENTORY_LIMIT")
    reply = {
        "requestRef": request, "nonce": nonce, "status": "SYNTHETIC_READ_ONLY",
        "counts": counts, "syntheticOnly": True,
        "adminAuthenticated": False, "trainingAllowed": False,
        "productionAllowed": False,
    }
    reply["mac"] = signed(key, request, nonce, reply["status"],
                          *(counts[k] for k in FIELDS))
    return reply


def main():
    try:
        if (os.environ.get("TARANOM_AI_WORKER_QA") != "CI_EPHEMERAL_ONLY" or
            os.environ.get("TARANOM_AI_OFFLINE_TEST_ONLY") != "CI_EPHEMERAL_ONLY"):
            return 17
        raw = os.environ.get("TARANOM_AI_WORKER_BRIDGE_TEST_KEY", "")
        if not re.fullmatch(r"[0-9a-f]{64}", raw) or raw == "0" * 64:
            return 17
        db = os.environ["TARANOM_AI_BRIDGE_TEST_DB"]
        if not os.path.realpath(db).startswith("/tmp/taranom-worker-"):
            return 17
        packet = sys.stdin.buffer.read(1025)
        if not 1 <= len(packet) <= 1024:
            return 17
        frame = json.loads(packet.decode("utf-8"), object_pairs_hook=unique)
        response = execute(frame, bytes.fromhex(raw), db)
        output = json.dumps(response, separators=(",", ":"), sort_keys=True)
        if len(output.encode("utf-8")) > 2048:
            return 17
        sys.stdout.write(output)
        sys.stdout.flush()
        return 0
    except (KeyError, ValueError, TypeError, UnicodeError, RegistryRejected,
            OSError, OverflowError):
        # No exception/SQL/secret/body can leak onto the parent logs.
        return 17


if __name__ == "__main__":
    raise SystemExit(main())
