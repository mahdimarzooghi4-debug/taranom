"""Offline, test-only local AI metadata CLI. No HTTP endpoint and no worker."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import sys
from .registry import OfflineRegistry, RegistryRejected

def _unique(pairs):
    out={}
    for key,value in pairs:
        if key in out:raise RegistryRejected("DUPLICATE_INPUT_JSON_KEY")
        out[key]=value
    return out

def main(args=None):
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--db",required=True)
    cmd=p.add_subparsers(dest="command",required=True)
    cmd.add_parser("init")
    listing=cmd.add_parser("inventory")
    listing.add_argument("--tenant",required=True)
    for name in ("freeze-dataset","register-model","evaluate",
                 "train-shadow","benchmark-shadow"):
        item=cmd.add_parser(name)
        item.add_argument("--input-json",required=True)
    a=p.parse_args(args)
    try:
        registry=OfflineRegistry(a.db)
        # Never accept arbitrary input as database commands, code, or URLs.
        if a.command=="init":
            registry.initialize()
            result={"status":"INITIALIZED_TEST_ONLY","production_allowed":False}
        elif a.command=="inventory":
            result=registry.read_inventory(a.tenant)
        else:
            f=Path(a.input_json)
            if f.is_symlink() or not f.is_file() or f.stat().st_size>65536:
                raise RegistryRejected("UNTRUSTED_JSON_INPUT_FILE")
            raw=json.loads(f.read_text("utf-8"),object_pairs_hook=_unique)
            from .training import fit, compare
            command={"freeze-dataset":registry.freeze_dataset,
                     "register-model":registry.register_model,
                     "evaluate":registry.evaluate_synthetic,
                     "train-shadow":lambda value:fit(registry,value),
                     "benchmark-shadow":lambda value:compare(registry,value)}[a.command]
            result=command(raw)
        print(json.dumps(result,sort_keys=True))
        return 0
    except (RegistryRejected,ValueError,OSError,UnicodeError,
            json.JSONDecodeError) as exc:
        print(json.dumps({"status":"DENIED","error":str(exc),
                          "production_allowed":False},sort_keys=True))
        return 2

if __name__=="__main__":
    raise SystemExit(main())
