#!/usr/bin/env python3
"""TRN-TECH-156: offline test-only Worker evidence export and receipt verification.

The *receiver process* checks a verifier-keyed receipt held in a separate
0700 fixture. It never trusts a receipt placed inside the exported ZIP.
Both sides still share a CI OS account: NOT independent custody or a real
recipient/service integration, and not authentication for Production.
"""
from __future__ import annotations

from io import BytesIO
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import stat
import sys
from zipfile import ZipFile, ZipInfo, ZIP_STORED, BadZipFile

import worker_evidence_anchor as anchor
from verify_ai_worker_evidence import verify_bytes

CONTRACT = "TRN-TECH-156"
ZIP_NAME = "worker-evidence.zip"
RECEIPT_NAME = "handoff-receipt.json"
MEMBERS = ("worker-evidence.jsonl", "handoff.json")
MANIFEST = {
    "contract", "recordCount", "bootCount", "lastEpoch", "headDigest",
    "ledgerSha256", "anchoredCount", "anchoredDigest",
    "anchoredEpoch", "anchorPrefixSha256", "auditEvidence",
    "independentCustody", "productionReady"
}
RECEIPT = {"contract","zipSha256","manifestSha256","ledgerSha256",
           "anchoredCount","headDigest","auditEvidence",
           "independentCustody","productionReady","mac"}
ZIP_MAX = 530000
HEX64 = r"[0-9A-F]{64}"
HEX32 = r"[0-9a-f]{32}"


def deny(code):
    raise ValueError("SYNTHETIC_HANDOFF_" + code)


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def required_hex(value, pattern):
    return isinstance(value,str) and re.fullmatch(pattern,value) is not None


def digest_receipt(secret, payload):
    return hmac.new(secret, b"TRN-TECH-156\n" + anchor.canonical(payload),
                    hashlib.sha256).hexdigest().upper()


def directory():
    if (os.environ.get("TARANOM_CENTER_DEMO_TEST_ONLY") != "CI_EPHEMERAL_ONLY"
        or os.environ.get("TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY") !=
        "CI_EPHEMERAL_ONLY"):
        deny("TEST_ONLY")
    folder=Path(os.environ.get("TARANOM_AI_WORKER_HANDOFF_DIR",""))
    if (not folder.is_absolute() or folder.parent != Path("/tmp") or
        not re.fullmatch(r"taranom-handoff-[A-Za-z0-9_-]{6,64}",folder.name)):
        deny("DIRECTORY")
    s=folder.lstat()
    if not stat.S_ISDIR(s.st_mode) or s.st_mode & 0o077:
        deny("DIRECTORY_PRIVILEGES")
    return folder


def custody():
    return anchor.directory()


def stored_zip(ledger, manifest):
    stream=BytesIO()
    with ZipFile(stream,mode="w",allowZip64=False) as archive:
        for name,contents in ((MEMBERS[0],ledger),(MEMBERS[1],manifest)):
            item=ZipInfo(name,date_time=(1980,1,1,0,0,0))
            item.create_system=3
            item.external_attr=(stat.S_IFREG | 0o600) << 16
            item.compress_type=ZIP_STORED
            archive.writestr(item,contents)
    content=stream.getvalue()
    if len(content)>ZIP_MAX:
        deny("ARCHIVE_SIZE")
    return content


def open_zip(data):
    if type(data) is not bytes or len(data)>ZIP_MAX or len(data)<100:
        deny("ARCHIVE_SIZE")
    with ZipFile(BytesIO(data),mode="r",allowZip64=False) as archive:
        infos=archive.infolist()
        if (len(infos)!=2 or
            [entry.filename for entry in infos]!=list(MEMBERS) or
            len({entry.filename for entry in infos})!=2):
            deny("ARCHIVE_CONTENT")
        for entry in infos:
            if (entry.is_dir() or entry.flag_bits & 1 or
                entry.compress_type!=ZIP_STORED or
                entry.file_size<=0 or entry.file_size>524288 or
                entry.compress_size!=entry.file_size or
                entry.create_system!=3 or
                stat.S_IFMT(entry.external_attr >> 16)!=stat.S_IFREG or
                stat.S_IMODE(entry.external_attr >> 16)!=0o600 or
                entry.filename.startswith("/") or ".." in Path(entry.filename).parts):
                deny("ARCHIVE_CONTENT")
        ledger=archive.read(MEMBERS[0])
        manifest=archive.read(MEMBERS[1])
    return ledger,manifest


def decode_manifest(data):
    if len(data)>2048 or not data.endswith(b"\n"):
        deny("MANIFEST_LIMIT")
    value=json.loads(data.decode("ascii",errors="strict"),
                     object_pairs_hook=anchor.unique)
    if (type(value) is not dict or set(value)!=MANIFEST or
        data!=anchor.canonical(value)+b"\n" or
        value["contract"]!=CONTRACT or
        value["auditEvidence"] is not False or
        value["independentCustody"] is not False or
        value["productionReady"] is not False or
        type(value["recordCount"]) is not int or
        type(value["bootCount"]) is not int or
        type(value["anchoredCount"]) is not int or
        value["recordCount"] < 5 or value["recordCount"] > 4096 or
        value["bootCount"]!=2 or
        value["anchoredCount"]!=value["recordCount"]-2 or
        not required_hex(value["lastEpoch"],HEX32) or
        not required_hex(value["anchoredEpoch"],HEX32) or
        any(not required_hex(value[k],HEX64) for k in (
            "headDigest","ledgerSha256","anchoredDigest","anchorPrefixSha256"))):
        deny("MANIFEST_INVALID")
    return value


def verify_bundle(data,receipt,secret):
    if (type(receipt) is not dict or set(receipt)!=RECEIPT or
        receipt["contract"]!=CONTRACT or
        receipt["auditEvidence"] is not False or
        receipt["independentCustody"] is not False or
        receipt["productionReady"] is not False or
        type(receipt["anchoredCount"]) is not int or
        any(not required_hex(receipt[k],HEX64) for k in (
            "zipSha256","manifestSha256","ledgerSha256","headDigest","mac"))):
        deny("RECEIPT_INVALID")
    fields={k:v for k,v in receipt.items() if k!="mac"}
    if not hmac.compare_digest(digest_receipt(secret,fields),receipt["mac"]):
        deny("RECEIPT_MAC")
    if not hmac.compare_digest(sha(data),receipt["zipSha256"]):
        deny("ARCHIVE_REPLACED")
    ledger,raw_manifest=open_zip(data)
    manifest=decode_manifest(raw_manifest)
    if (not hmac.compare_digest(sha(raw_manifest),receipt["manifestSha256"]) or
        not hmac.compare_digest(sha(ledger),receipt["ledgerSha256"]) or
        manifest["ledgerSha256"]!=receipt["ledgerSha256"] or
        manifest["headDigest"]!=receipt["headDigest"] or
        manifest["anchoredCount"]!=receipt["anchoredCount"]):
        deny("MANIFEST_RECEIPT_CONFLICT")
    rows,boots,last=verify_bytes(ledger)
    n=manifest["anchoredCount"]
    if (len(rows)!=manifest["recordCount"] or len(boots)!=2 or
        last!=manifest["lastEpoch"] or
        boots[0]!=manifest["anchoredEpoch"] or
        boots[1]==boots[0] or
        sha(ledger)!=manifest["ledgerSha256"] or
        sha(("\n".join(rows[:n])+"\n").encode("utf-8")) !=
        manifest["anchorPrefixSha256"]):
        deny("HISTORY_MISMATCH")
    original=json.loads(rows[n-1],object_pairs_hook=anchor.unique)
    boot=json.loads(rows[n],object_pairs_hook=anchor.unique)
    recovered=json.loads(rows[n+1],object_pairs_hook=anchor.unique)
    final=json.loads(rows[-1],object_pairs_hook=anchor.unique)
    if (original["digest"]!=manifest["anchoredDigest"] or
        boot["previousDigest"]!=manifest["anchoredDigest"] or
        boot["epoch"]!=boots[1] or boot["kind"]!="Boot" or
        recovered["epoch"]!=boots[1] or recovered["kind"]!="Success" or
        final["digest"]!=manifest["headDigest"]):
        deny("LINEAGE_MISMATCH")
    return {"recordsVerified":len(rows),"processEpochs":2,
            "auditEvidence":False,"independentCustody":False,
            "productionReady":False}


def export(folder,holder):
    anchor.verify_continuation(holder)
    rows,boots,last=anchor.read_records()
    ledger=anchor.evidence_path().read_bytes()
    if len(rows)<5 or len(boots)!=2:
        deny("EXPORT_HISTORY")
    anchored=json.loads(anchor.checked_file(holder/"anchor.json",2048),
                        object_pairs_hook=anchor.unique)
    n=anchored["anchoredCount"]
    final=json.loads(rows[-1],object_pairs_hook=anchor.unique)
    manifest={
        "contract":CONTRACT,"recordCount":len(rows),"bootCount":len(boots),
        "lastEpoch":last,"headDigest":final["digest"],
        "ledgerSha256":sha(ledger),"anchoredCount":n,
        "anchoredDigest":anchored["anchoredDigest"],
        "anchoredEpoch":anchored["anchoredEpoch"],
        "anchorPrefixSha256":anchored["prefixSha256"],
        "auditEvidence":False,"independentCustody":False,
        "productionReady":False
    }
    content=stored_zip(ledger,anchor.canonical(manifest)+b"\n")
    key=anchor.checked_file(holder/"custodian.key",32)
    if len(key)!=32:
        deny("HOLDER_KEY")
    raw_manifest=anchor.canonical(manifest)+b"\n"
    receipt_fields={
        "contract":CONTRACT, "zipSha256":sha(content),
        "manifestSha256":sha(raw_manifest), "ledgerSha256":sha(ledger),
        "anchoredCount":n, "headDigest":final["digest"],
        "auditEvidence":False, "independentCustody":False,
        "productionReady":False
    }
    signed={**receipt_fields,"mac":digest_receipt(key,receipt_fields)}
    # Test both generated objects before committing a one-time handoff.
    verify_bundle(content,signed,key)
    anchor.fresh_file(folder/ZIP_NAME,content)
    anchor.fresh_file(holder/RECEIPT_NAME,anchor.canonical(signed)+b"\n")
    return len(rows)


def receive(folder,holder):
    key=anchor.checked_file(holder/"custodian.key",32)
    if len(key)!=32:
        deny("HOLDER_KEY")
    receipt_bytes=anchor.checked_file(holder/RECEIPT_NAME,2048)
    receipt=json.loads(receipt_bytes.decode("ascii",errors="strict"),
                       object_pairs_hook=anchor.unique)
    if receipt_bytes!=anchor.canonical(receipt)+b"\n":
        deny("NONCANONICAL_RECEIPT")
    packed=anchor.checked_file(folder/ZIP_NAME,ZIP_MAX)
    return verify_bundle(packed,receipt,key)


def main():
    if len(sys.argv)!=2 or sys.argv[1] not in ("export","receive"):
        deny("COMMAND")
    folder=directory()
    holder=custody()
    if sys.argv[1]=="export":
        export(folder,holder)
        print("PASS: synthetic Worker archive exported with separate verifier-held receipt; no real custody")
    else:
        result=receive(folder,holder)
        if result["auditEvidence"] or result["independentCustody"] or result["productionReady"]:
            deny("FALSE_AUTHORITY")
        print("PASS: separate-process offline receipt verified exact synthetic two-epoch archive; no Production authority")


if __name__=="__main__":
    try:
        main()
    except (AssertionError,OSError,ValueError,KeyError,UnicodeError,TypeError,
            BadZipFile,OverflowError):
        print("SYNTHETIC_HANDOFF_DENIED",file=sys.stderr)
        raise SystemExit(17) from None
