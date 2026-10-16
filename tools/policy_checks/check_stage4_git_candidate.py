#!/usr/bin/env python3
"""TRN-TECH-140: read-only Git DAG attestation of an unmerged Stage4 test candidate.

No GitHub API writes, git fetch, branch mutations, merge, deploy, remote
credentials or network access. This proves at most local checkout ancestry
and exact tracked remote refs; NEVER review, Stage, QA or release admission.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys

MANIFEST = Path("docs/governance/stage4-premerge-candidate.json")
SOURCE = "TRN-TECH-140"
TARGET_BRANCH = "sprint-131-stage4-premerge-git-candidate"
CHAIN = (132,133,134,135,136,137,138,139,140,141,143)
REF = re.compile(r"sprint-[0-9]{2,3}-[a-z0-9-]{4,80}\Z")
SHA = re.compile(r"[0-9a-f]{40}\Z")
EXPECTED = frozenset(("schema_version","contract","candidate_branch",
    "source_context","base_main_sha","chain",
    "stage4_test_authorized","production_release_allowed"))
ENTRY = frozenset(("pr","branch","base","head_sha",
                   "merged","independently_approved"))

class InvalidCandidate(Exception):
    pass

def unique_json(pairs):
    out = {}
    for key,value in pairs:
        if key in out:
            raise ValueError("DUPLICATE_JSON_KEY")
        out[key] = value
    return out

def read_manifest(path: Path):
    if path.is_symlink() or not path.is_file() or path.stat().st_size>20000:
        raise InvalidCandidate("MANIFEST_MISSING_OR_UNTRUSTED")
    try:
        doc=json.loads(path.read_text("utf-8"),object_pairs_hook=unique_json)
    except (ValueError,TypeError,UnicodeError,OSError):
        raise InvalidCandidate("MANIFEST_JSON_INVALID")
    if type(doc) is not dict or set(doc)!=EXPECTED or type(doc.get("schema_version")) is not int or doc["schema_version"]!=1 or doc.get("contract")!=SOURCE or doc.get("candidate_branch")!=TARGET_BRANCH or doc.get("source_context")!="UNMERGED_GITHUB_BRANCH_TIPS_NOT_APPROVED" or doc.get("stage4_test_authorized") is not False or doc.get("production_release_allowed") is not False or type(doc.get("base_main_sha")) is not str or not SHA.fullmatch(doc["base_main_sha"]):
        raise InvalidCandidate("MANIFEST_AUTHORITY_OR_SCHEMA_INVALID")
    chain=doc.get("chain")
    if type(chain) is not list or len(chain)!=len(CHAIN):
        raise InvalidCandidate("INCOMPLETE_PR_CHAIN")
    prev="main"
    for index,entry in enumerate(chain):
        if type(entry) is not dict or set(entry)!=ENTRY or type(entry.get("pr")) is not int or entry["pr"]!=CHAIN[index] or type(entry.get("branch")) is not str or not REF.fullmatch(entry["branch"]) or type(entry.get("base")) is not str or entry["base"]!=prev or type(entry.get("head_sha")) is not str or not SHA.fullmatch(entry["head_sha"]) or entry.get("merged") is not False or entry.get("independently_approved") is not False:
            raise InvalidCandidate("PR_CHAIN_ORDER_OR_AUTHORITY_INVALID")
        prev=entry["branch"]
    if len({x["branch"] for x in chain})!=len(chain) or len({x["head_sha"] for x in chain})!=len(chain):
        raise InvalidCandidate("DUPLICATE_PR_BRANCH_OR_HEAD")
    return doc

def git(repo: Path,*args: str,allow_no_ancestor=False):
    try:
        r=subprocess.run(["git","-C",str(repo),*args],capture_output=True,
                         timeout=20,check=False)
    except (OSError,subprocess.TimeoutExpired) as e:
        raise InvalidCandidate("LOCAL_GIT_UNAVAILABLE") from e
    if allow_no_ancestor and r.returncode==1:
        return False
    if r.returncode:
        raise InvalidCandidate("LOCAL_GIT_QUERY_FAILED")
    if allow_no_ancestor:
        return True
    try:
        return r.stdout.decode("ascii").strip()
    except UnicodeDecodeError as e:
        raise InvalidCandidate("GIT_REVISION_NOT_ASCII") from e

def probe(repo: Path):
    """Return only preexisting local Git refs; no fetch or writes."""
    return (
        lambda ref: git(repo,"rev-parse","--verify",ref+"^{commit}"),
        lambda parent,child: git(repo,"merge-base","--is-ancestor",parent,child,
                                  allow_no_ancestor=True),
        lambda: git(repo,"rev-parse","--verify","HEAD^{tree}"),
    )

def assess(doc,resolve,is_ancestor,tree,*,event,ref_name,sha):
    errors=[]
    resolved=[]
    # Synthetic-only Git premerge proof runs ONLY for exact push context.
    if event!="push" or ref_name!=TARGET_BRANCH or not isinstance(sha,str) or not SHA.fullmatch(sha):
        errors.append("PUSH_CONTEXT_UNVERIFIABLE")
    try:
        main=resolve("refs/remotes/origin/main")
        actual=resolve("HEAD")
        tree_sha=tree()
    except InvalidCandidate as err:
        errors.append(str(err))
        main=actual=tree_sha=None
    if main is not None and main!=doc["base_main_sha"]:
        errors.append("MAIN_BASE_MOVED")
    if actual is not None and actual!=sha:
        errors.append("CHECKOUT_NOT_EXACT_PUSH_HEAD")
    if tree_sha is not None and not SHA.fullmatch(tree_sha):
        errors.append("TREE_HASH_UNVERIFIABLE")
    prior=doc["base_main_sha"]
    for entry in doc["chain"]:
        remote="refs/remotes/origin/"+entry["branch"]
        try:
            current=resolve(remote)
        except InvalidCandidate:
            errors.append("PR_BRANCH_NOT_FETCHED_OR_MISSING")
            break
        if current!=entry["head_sha"]:
            errors.append("PR_BRANCH_HEAD_CHANGED")
        resolved.append({"pr":entry["pr"],"branch":entry["branch"],
                         "pinned_sha":entry["head_sha"],
                         "fetched_tip_matches_pin":current==entry["head_sha"]})
        try:
            if not is_ancestor(prior,entry["head_sha"]):
                errors.append("STACKED_BRANCH_NOT_DESCENDANT")
        except InvalidCandidate:
            errors.append("ANCESTRY_UNVERIFIABLE")
        prior=entry["head_sha"]
    if actual is not None:
        try:
            if not is_ancestor(prior,actual) or actual==prior:
                errors.append("CANDIDATE_HEAD_MISSING_SOURCE_STACK")
        except InvalidCandidate:
            errors.append("CANDIDATE_ANCESTRY_UNVERIFIABLE")
    return {
        "contract":SOURCE,
        "status":"PREMERGE_GIT_ANCESTRY_CONSISTENT" if not errors else "DENIED_UNVERIFIABLE",
        "local_git_ancestry_verified":not errors,
        "github_pr_states_checked_live":False,
        "independent_code_review_verified":False,
        "stage4_test_admission":"DENIED",
        "stage_deployment_authorized":False,
        "production_release_allowed":False,
        "candidate_branch":TARGET_BRANCH,
        "candidate_commit":actual,
        "candidate_tree_sha":tree_sha,
        "pinned_main_sha":doc["base_main_sha"],
        "pr_heads":resolved,
        "findings":sorted(set(errors)),
    }

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root",type=Path,default=Path("."))
    parser.add_argument("--report-json",type=Path,required=True)
    a=parser.parse_args(argv)
    try:
        root=a.root.resolve(strict=True)
        doc=read_manifest(root/MANIFEST)
        if os.environ.get("GITHUB_EVENT_NAME")!="push":
            # PR merge refs are not the submitted branch commit. Never
            # make an assertion about exact PR ancestry from that checkout,
            # but still produce an explicitly non-authorizing CI artifact.
            report={"contract":SOURCE,"status":"NOT_ATTESTED_NONPUSH_CONTEXT",
                    "local_git_ancestry_verified":False,
                    "github_pr_states_checked_live":False,
                    "stage4_test_admission":"DENIED",
                    "stage_deployment_authorized":False,
                    "production_release_allowed":False,
                    "findings":[]}
        else:
            resolver,ancestry,tree=probe(root)
            report=assess(doc,resolver,ancestry,tree,
                          event=os.environ.get("GITHUB_EVENT_NAME"),
                          ref_name=os.environ.get("GITHUB_REF_NAME"),
                          sha=os.environ.get("GITHUB_SHA"))
    except (OSError,InvalidCandidate) as e:
        report={"contract":SOURCE,"status":"DENIED_UNVERIFIABLE",
                "local_git_ancestry_verified":False,"stage4_test_admission":"DENIED",
                "stage_deployment_authorized":False,"production_release_allowed":False,
                "findings":[str(e)]}
    try:
        if a.report_json.is_symlink():
            raise OSError("SYMLINK_OUTPUT")
        a.report_json.parent.mkdir(parents=True,exist_ok=True)
        a.report_json.write_text(json.dumps(report,indent=2,sort_keys=True)+"\n",
                                encoding="utf-8")
    except OSError:
        print("PREMERGE PROOF DENIED: REPORT_UNWRITABLE",file=sys.stderr)
        return 1
    print("Git candidate: "+report["status"]+"; Stage4=DENIED; merge=NOT_PERMITTED")
    for problem in report["findings"]:
        print("DENIED "+problem,file=sys.stderr)
    return 0 if report["status"] in (
        "PREMERGE_GIT_ANCESTRY_CONSISTENT",
        "NOT_ATTESTED_NONPUSH_CONTEXT") else 1

if __name__=="__main__":
    raise SystemExit(main())
