#!/usr/bin/env python3
"""TRN-TECH-120: offline build-input declaration inventory, NOT SBOM attestation."""
from __future__ import annotations
import argparse
import base64
import binascii
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import xml.etree.ElementTree as ET

SDK = re.compile(r"\d+\.\d+\.\d+\Z")
VERSION = re.compile(r"\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?\Z")
ACTION = re.compile(r"^\s*(?:-\s*)?uses:\s*(\S+)(?:\s+#.*)?$", re.M)
PIN = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}\Z")
NPM_SHA512 = re.compile(r"sha512-([A-Za-z0-9+/]+={0,2})\Z")
IMAGE = re.compile(r"^\s*image:\s*(postgres:[^\s#]+)\s*(?:#.*)?$", re.M)
APPS = ("member-web","center-web","ops-web","member-support-demo-web","center-demo-web")

class SourceError(Exception): pass

def unique_pairs(items):
    data = {}
    for name, value in items:
        if name in data: raise SourceError("DUPLICATE_JSON_KEY")
        data[name] = value
    return data

def read(root: Path, rel: str, maximum: int) -> bytes:
    if not rel or rel.startswith("/") or any(x in ("",".","..") for x in rel.split("/")):
        raise SourceError("UNSAFE_SOURCE_PATH")
    target = root
    for part in rel.split("/"):
        target = target / part
        if stat.S_ISLNK(os.lstat(target).st_mode):
            raise SourceError("SYMLINKED_SOURCE")
    if not target.is_file() or target.stat().st_size > maximum:
        raise SourceError("INVALID_SOURCE_FILE")
    data = target.read_bytes()
    if not data or len(data) > maximum: raise SourceError("INVALID_SOURCE_SIZE")
    return data

def parsed(data: bytes):
    return json.loads(data, object_pairs_hook=unique_pairs)

def inspect(root: Path) -> dict:
    report = dict(contract="TRN-TECH-120",status="SOURCE_UNVERIFIABLE",
        scope="SOURCE_DECLARATIONS_ONLY",sourceFingerprint=None,
        npmApplications=0,npmLockedPackages=0,nugetPackageDeclarations=0,
        dotnetSdkVersion=None,pinnedGithubActions=0,containerReferences=0,
        mutableContainerTags=0,nugetRestoreBytesLocked=False,
        containerDigestPinned=False,dependencyVulnerabilityReviewCompleted=False,
        supplyChainAttested=False,productionReady=False,nationalReleaseAllowed=False,
        errors=[])
    try:
        root = root.resolve(strict=True)
        if not root.is_dir(): raise SourceError("INVALID_ROOT")
        sources = {}
        def fetch(rel, cap=512*1024):
            data = read(root, rel, cap)
            sources[rel] = hashlib.sha256(data).hexdigest()
            return data
        sdk = parsed(fetch("global.json"))
        if (not isinstance(sdk,dict) or not isinstance(sdk.get("sdk"),dict)
            or not SDK.fullmatch(str(sdk["sdk"].get("version","")))
            or sdk["sdk"].get("rollForward")!="disable"):
            raise SourceError("UNPINNED_DOTNET_SDK")
        report["dotnetSdkVersion"] = sdk["sdk"]["version"]
        for app in APPS:
            base = "apps/"+app
            package = parsed(fetch(base+"/package.json"))
            locked = parsed(fetch(base+"/package-lock.json",8*1024*1024))
            if (not isinstance(package,dict) or not isinstance(locked,dict)
                or locked.get("lockfileVersion") != 3
                or not isinstance(locked.get("packages"),dict)
                or not isinstance(locked["packages"].get(""),dict)):
                raise SourceError("INVALID_NPM_LOCK")
            header = locked["packages"][""]
            if (header.get("name")!=package.get("name")
                or header.get("version")!=package.get("version")):
                raise SourceError("STALE_NPM_ROOT")
            for label in ("dependencies","devDependencies"):
                deps = package.get(label,{})
                if (not isinstance(deps,dict) or not deps
                    or any(not isinstance(v,str) or not VERSION.fullmatch(v)
                           for v in deps.values())):
                    raise SourceError("FLOATING_NPM_DIRECT_DEPENDENCY")
                if header.get(label,{})!=deps:
                    raise SourceError("STALE_NPM_DIRECT_DEPENDENCY")
                # Matching root metadata is insufficient: direct npm inputs
                # must exist in the actual lock package entries.
                for direct_name, direct_version in deps.items():
                    direct = locked["packages"].get("node_modules/"+direct_name)
                    if (not isinstance(direct,dict)
                            or direct.get("version")!=direct_version):
                        raise SourceError("DIRECT_NPM_PACKAGE_NOT_LOCKED")
            for name, body in locked["packages"].items():
                if not name: continue
                if (not name.startswith("node_modules/") or not isinstance(body,dict)
                    or not VERSION.fullmatch(str(body.get("version","")))
                    or not isinstance(body.get("resolved"),str)
                    or not body["resolved"].startswith("https://registry.npmjs.org/")):
                    raise SourceError("UNPINNED_NPM_PACKAGE_SOURCE")
                match = NPM_SHA512.fullmatch(str(body.get("integrity","")))
                if not match: raise SourceError("MISSING_NPM_INTEGRITY")
                try: digest = base64.b64decode(match.group(1),validate=True)
                except (ValueError,binascii.Error) as exc:
                    raise SourceError("INVALID_NPM_INTEGRITY") from exc
                if len(digest)!=64: raise SourceError("INVALID_NPM_INTEGRITY")
                report["npmLockedPackages"] += 1
            report["npmApplications"] += 1
        projects = sorted(p for folder in (root/"src",root/"tests")
                          for p in folder.rglob("*.csproj"))
        if not projects: raise SourceError("MISSING_DOTNET_PROJECTS")
        for project in projects:
            doc = ET.fromstring(fetch(project.relative_to(root).as_posix()))
            for package in doc.iter("PackageReference"):
                name = package.get("Include")
                ver = package.get("Version") or package.findtext("Version")
                if not name or not ver or not VERSION.fullmatch(ver):
                    raise SourceError("FLOATING_NUGET_PACKAGE_VERSION")
                report["nugetPackageDeclarations"] += 1
        if not report["nugetPackageDeclarations"]:
            raise SourceError("MISSING_NUGET_DEPENDENCIES")
        workflow = fetch(".github/workflows/taranom-full-regression-reusable.yml").decode("utf-8")
        actions = ACTION.findall(workflow)
        if not actions or any(not PIN.fullmatch(a) for a in actions):
            raise SourceError("UNPINNED_GITHUB_ACTION")
        report["pinnedGithubActions"] = len(actions)
        images = IMAGE.findall(workflow)
        if not images: raise SourceError("NO_DATABASE_TEST_SOURCE")
        report["containerReferences"] = len(images)
        report["mutableContainerTags"] = sum("@sha256:" not in img for img in images)
        report["containerDigestPinned"] = report["mutableContainerTags"]==0
        fingerprint = hashlib.sha256()
        for rel in sorted(sources):
            fingerprint.update((rel+"\0"+sources[rel]+"\n").encode())
        report["sourceFingerprint"] = fingerprint.hexdigest()
        report["status"] = "DECLARED_BUILD_INPUTS_CHECKED_NOT_ATTESTED"
    except (SourceError,OSError,ValueError,TypeError,KeyError,
            ET.ParseError,UnicodeError) as exc:
        report["errors"]=[str(exc) if isinstance(exc,SourceError)
                          else "SOURCE_READ_OR_PARSE_FAILED"]
        report["sourceFingerprint"]=None
    return report

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root",type=Path,default=Path("."))
    parser.add_argument("--report-json",type=Path,required=True)
    args=parser.parse_args(argv)
    result=inspect(args.root)
    args.report_json.parent.mkdir(parents=True,exist_ok=True)
    args.report_json.write_text(json.dumps(result,indent=2,sort_keys=True)+"\n",encoding="utf-8")
    print("Build source inventory: "+result["status"]+
          "; mutable_containers="+str(result["mutableContainerTags"])+
          "; no vulnerability audit, signature, real provenance or release approval.")
    return 0 if result["status"]=="DECLARED_BUILD_INPUTS_CHECKED_NOT_ATTESTED" else 1

if __name__=="__main__":
    sys.exit(main())
