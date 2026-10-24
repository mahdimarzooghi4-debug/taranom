#!/usr/bin/env python3
"""TRN-TECH-157: isolated CI Linux UID exercises synthetic Worker custody.

This command is invoked only by GitHub's existing disposable full regression
under the normal non-root runner, whose sudo permission is a *known limit*.
It adds no system account in Stage/Production and never handles real data.
"""
from __future__ import annotations

import os
from pathlib import Path
import pwd
import re
import shutil
import stat
import subprocess
import sys
import tempfile

from verify_ai_worker_evidence import verify_bytes

USER = "taranom_custody_qa"
ROOT = Path(__file__).resolve().parents[2]
ENV_ARGS = ("TARANOM_CENTER_DEMO_TEST_ONLY",
            "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY")
GATE = "CI_EPHEMERAL_ONLY"


def deny(code: str) -> None:
    raise ValueError("SYNTHETIC_POSIX_CUSTODY_" + code)


def guard() -> None:
    if os.geteuid() == 0:
        deny("RUNNER_MUST_BE_NONROOT")
    for name in ENV_ARGS:
        if os.environ.get(name) != GATE:
            deny("TEST_ONLY")
    if sys.platform != "linux" or os.environ.get("GITHUB_ACTIONS") != "true":
        deny("CI_LINUX_ONLY")


def folder(name: str,prefix: str) -> Path:
    raw=os.environ.get(name, "")
    target=Path(raw)
    if (not target.is_absolute() or target.parent != Path("/tmp") or
        re.fullmatch(r"taranom-"+prefix+r"-[A-Za-z0-9_-]{6,64}",
                     target.name) is None):
        deny("FOLDER_SCOPE")
    info=target.lstat()
    mode = stat.S_IMODE(info.st_mode)
    permitted=(0o700,0o755) if prefix=="worker" else (
        (0o700,0o555) if prefix=="custody-tools" else (0o700,))
    if not stat.S_ISDIR(info.st_mode) or mode not in permitted:
        deny("FOLDER_MODE")
    return target


def original() -> Path:
    path=Path(os.environ.get("TARANOM_AI_WORKER_EVIDENCE_FILE",""))
    if (not path.is_absolute() or path.parent.parent!=Path("/tmp") or
        re.fullmatch(r"taranom-worker-[A-Za-z0-9_-]{6,64}",
                     path.parent.name) is None or
        path.name != "worker-evidence.jsonl" or path.is_symlink()):
        deny("ORIGINAL_SCOPE")
    return path


def command(args, expect_success=True):
    result=subprocess.run(args,stdout=subprocess.PIPE,stderr=subprocess.PIPE,
                          check=False,timeout=30)
    if (result.returncode==0)!=expect_success:
        # Only fixed command phase, never stdout/stderr, key or argv.
        if "useradd" in args:
            deny("CREATE_USER")
        if "chown" in args:
            deny("SET_OWNER")
        if "/usr/bin/python3" in args:
            # Only static category from the verifier's already-redacted
            # stderr; never forward path, key or trace contents.
            if b"SYNTHETIC_ANCHOR_VERIFICATION_DENIED" in result.stderr:
                deny("ANCHOR_INPUT_REJECTED")
            if b"SYNTHETIC_HANDOFF_DENIED" in result.stderr:
                deny("HANDOFF_INPUT_REJECTED")
            if b"Permission denied" in result.stderr or b"can't open file" in result.stderr:
                deny("VERIFIER_PATH_UNREADABLE")
            deny("VERIFIER_EXECUTION")
        if "install" in args:
            deny("HANDOFF_INSTALL")
        if "/usr/bin/test" in args:
            deny("PERMISSION_ASSERTION")
        deny("ISOLATION_COMMAND")
    return result.returncode


def custodian(program: str, operation: str, *, handoff: Path|None=None):
    if program not in {"worker_evidence_anchor.py",
                        "worker_evidence_handoff.py"}:
        deny("PROGRAM")
    scripts=folder("TARANOM_QA_TOOLS_DIR","custody-tools")
    if scripts.stat().st_uid!=0 or stat.S_IMODE(scripts.stat().st_mode)!=0o555:
        deny("VERIFIER_CODE_OWNER")
    source=scripts/program
    if (source.is_symlink() or source.stat().st_uid!=0 or
        stat.S_IMODE(source.stat().st_mode)!=0o444):
        deny("VERIFIER_CODE_MUTABLE")
    shared=folder("TARANOM_QA_SHARED_DIR","worker")
    anchor=folder("TARANOM_QA_CUSTODY_DIR","anchor")
    env=[
        "PATH=/usr/bin:/bin",
        "TARANOM_CENTER_DEMO_TEST_ONLY="+GATE,
        "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY="+GATE,
        "TARANOM_AI_WORKER_EVIDENCE_FILE="+str(shared/"worker-evidence.jsonl"),
        "TARANOM_AI_WORKER_ANCHOR_DIR="+str(anchor),
    ]
    if handoff is not None:
        env.append("TARANOM_AI_WORKER_HANDOFF_DIR="+str(handoff))
    command(["sudo","-n","-u",USER,"/usr/bin/env","-i",*env,
             "/usr/bin/python3",str(source),operation])


def inaccessible(path:Path):
    try:
        fd=os.open(path,os.O_RDONLY|getattr(os,"O_NOFOLLOW",0))
    except PermissionError:
        return
    except FileNotFoundError:
        deny("PROOF_MISSING")
    else:
        os.close(fd)
        deny("UNAUTHORIZED_KEY_READ")


def publish(source:Path,dest:Path,create:bool):
    raw=source.read_bytes()
    verify_bytes(raw)
    if create:
        fd=os.open(dest,os.O_WRONLY|os.O_CREAT|os.O_EXCL|
                   getattr(os,"O_NOFOLLOW",0),0o600)
        with os.fdopen(fd,"wb") as writer:
            writer.write(raw)
            writer.flush()
            os.fsync(writer.fileno())
    else:
        if dest.is_symlink() or not dest.is_file():
            deny("RELAY_TAMPER")
        # The trusted runner owns this *public synthetic* staging file.
        os.chmod(dest,0o600)
        with dest.open("wb") as writer:
            writer.write(raw)
            writer.flush()
            os.fsync(writer.fileno())
    os.chmod(dest,0o444)


def prepare():
    shared=folder("TARANOM_QA_SHARED_DIR","worker")
    anchor=folder("TARANOM_QA_CUSTODY_DIR","anchor")
    scripts=folder("TARANOM_QA_TOOLS_DIR","custody-tools")
    if (shared.stat().st_uid != os.geteuid() or
        anchor.stat().st_uid != os.geteuid() or
        scripts.stat().st_uid != os.geteuid()):
        deny("UNEXPECTED_INITIAL_OWNER")
    # Stage ONLY 3 exact verifier scripts into a root-owned directory.
    # Unlike an ordinary copy into runner-owned space, the non-root
    # Worker account cannot rewrite code before it is interpreted.
    for name in ("worker_evidence_anchor.py",
                 "worker_evidence_handoff.py",
                 "verify_ai_worker_evidence.py"):
        copied=scripts/name
        shutil.copyfile(ROOT/"tools"/"qa"/name,copied)
        os.chmod(copied,0o444)
        command(["sudo","-n","chown","root:root",str(copied)])
    command(["sudo","-n","chown","root:root",str(scripts)])
    command(["sudo","-n","chmod","0555",str(scripts)])
    # The GitHub runner is sudo-capable; this is still a real Linux
    # DAC check for *unprivileged* caller processes, not root isolation.
    command(["sudo","-n","useradd","--system","--user-group",
             "--no-create-home","--shell","/usr/sbin/nologin",USER])
    os.chmod(shared,0o755)
    command(["sudo","-n","chown",USER, str(anchor)])
    publish(original(),shared/"worker-evidence.jsonl",True)
    custodian("worker_evidence_anchor.py","create")
    inaccessible(anchor/"custodian.key")
    command(["sudo","-n","-u",USER,"/usr/bin/test","-w",
             str(shared/"worker-evidence.jsonl")],expect_success=False)
    print("PASS: distinct unprivileged Linux custodian sealed pre-restart synthetic anchor; "
          "runner cannot read key without sudo")


def finish():
    shared=folder("TARANOM_QA_SHARED_DIR","worker")
    anchor=folder("TARANOM_QA_CUSTODY_DIR","anchor")
    account=pwd.getpwnam(USER)
    if (shared.stat().st_uid != os.geteuid() or
        anchor.stat().st_uid != account.pw_uid):
        deny("OWNER_CHANGED")
    inaccessible(anchor/"custodian.key")
    publish(original(),shared/"worker-evidence.jsonl",False)
    custodian("worker_evidence_anchor.py","verify")
    outs=[]
    for _ in range(2):
        new=Path(tempfile.mkdtemp(prefix="taranom-handoff-",dir="/tmp"))
        command(["sudo","-n","chown",USER,str(new)])
        outs.append(new)
    sent,received=outs
    custodian("worker_evidence_handoff.py","export",handoff=sent)
    # Only root-controlled *copy* of a synthetic ZIP crosses directories.
    # Secret and signer-side receipt remain in 0700 custodian directory.
    command(["sudo","-n","install","-m","0600","-o",USER,"-g",USER,
             str(sent/"worker-evidence.zip"),str(received/"worker-evidence.zip")])
    custodian("worker_evidence_handoff.py","receive",handoff=received)
    inaccessible(anchor/"custodian.key")
    inaccessible(anchor/"handoff-receipt.json")
    # A normal Kestrel/runner UID cannot read or replace the custodian key,
    # nor can the receiving process claim Production/clinical authority.
    print("PASS: separate Linux UID verified actual two-epoch Worker evidence "
          "handoff, denied runner key/receipt access; NOT root-independent custody")


def main():
    guard()
    if len(sys.argv)!=2 or sys.argv[1] not in ("prepare","finish"):
        deny("COMMAND")
    if sys.argv[1]=="prepare":
        prepare()
    else:
        finish()


if __name__=="__main__":
    try:
        main()
    except (OSError,ValueError,AssertionError,KeyError,
            subprocess.TimeoutExpired) as ex:
        # Names can only be hard-coded classifications defined by deny().
        label=str(ex) if isinstance(ex,ValueError) and str(ex).startswith(
            "SYNTHETIC_POSIX_CUSTODY_") else "SYNTHETIC_POSIX_CUSTODY_DENIED"
        print(label,file=sys.stderr)
        raise SystemExit(17) from None
