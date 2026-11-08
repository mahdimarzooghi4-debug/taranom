#!/usr/bin/env python3
"""Bounded byte inventory for protected same-origin patient/center UI builds.

This is a distinct verifier from check_disconnected_bundle.py: a protected
panel legitimately uses same-origin API calls. Do not weaken or repurpose the
disconnected-shell checker. Neither check is Release/Stage authorization.
"""
from __future__ import annotations

import argparse
import hashlib
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import re
import sys

SURFACES = {"member", "center"}
EXTENSIONS = {".html", ".js", ".css", ".svg", ".png", ".jpg", ".jpeg",
              ".webp", ".gif", ".ico", ".woff", ".woff2", ".webmanifest", ".txt"}
RESOURCE = re.compile(r"\A/[A-Za-z0-9._/-]+\Z")
CSS_URL = re.compile(r"(?i)\burl\(\s*([^)]*)\s*\)")
MAX_FILE = 20 * 1024 * 1024
MAX_TOTAL = 64 * 1024 * 1024


def local_path(value: str) -> str:
    if not isinstance(value, str) or not RESOURCE.fullmatch(value):
        raise ValueError("remote, dynamic or encoded bundle resource")
    parts = value[1:].split("/")
    if not parts or any(part in {"", ".", ".."} for part in parts):
        raise ValueError("noncanonical resource path")
    return "/".join(parts)


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate manifest key")
        result[key] = value
    return result


def bundle_files(dist: Path) -> dict[str, Path]:
    if dist.is_symlink() or not dist.is_dir():
        raise ValueError("unsafe distribution root")
    result = {}
    total = 0
    for home, dirs, names in os.walk(dist, followlinks=False):
        for subdir in dirs:
            if (Path(home) / subdir).is_symlink():
                raise ValueError("linked distribution directory")
        for name in names:
            path = Path(home) / name
            if path.is_symlink() or not path.is_file() or path.suffix.lower() not in EXTENSIONS:
                raise ValueError("unapproved, nonregular or linked distribution file")
            rel = path.relative_to(dist).as_posix()
            if rel.endswith(".map") or rel in result:
                raise ValueError("source map or duplicate distribution path")
            size = path.stat().st_size
            if size > MAX_FILE:
                raise ValueError("oversized distribution file")
            total += size
            if total > MAX_TOTAL:
                raise ValueError("oversized distribution")
            result[rel] = path
    if "index.html" not in result or len(result) < 3:
        raise ValueError("missing built HTML/JS/CSS")
    return result


def csp_is_same_origin(csp: str) -> bool:
    directives = {}
    for chunk in csp.split(";"):
        tokens = chunk.strip().split()
        if not tokens:
            continue
        name = tokens[0].lower()
        if name in directives:
            return False
        directives[name] = tokens[1:]
    exact = {
        "default-src": ["'none'"], "base-uri": ["'none'"],
        "object-src": ["'none'"], "frame-src": ["'none'"],
        "form-action": ["'none'"], "script-src": ["'self'"],
        "style-src": ["'self'"], "font-src": ["'self'"],
        "connect-src": ["'self'"], "worker-src": ["'none'"],
        "media-src": ["'none'"],
    }
    if any(directives.get(k) != v for k, v in exact.items()):
        return False
    if directives.get("img-src") not in (["'self'", "data:"], ["'self'"]):
        return False
    if "manifest-src" in directives and directives["manifest-src"] != ["'self'"]:
        return False
    if any(k not in {*exact, "img-src", "manifest-src"} for k in directives):
        return False
    return True


class Entry(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.lang_ok = False
        self.csp = None
        self.resources = set()
        self.modules = 0
        self.styles = 0
        self.manifests = 0
        self.in_script = False

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if len(a) != len(attrs) or any(k.lower().startswith("on") for k in a):
            raise ValueError("duplicate attribute or inline event")
        if tag == "html":
            self.lang_ok = a.get("lang") == "fa" and a.get("dir") == "rtl"
        if tag in {"base", "iframe", "frame", "object", "embed", "form",
                   "img", "audio", "video", "source", "image", "use"}:
            raise ValueError("unreviewed entrypoint element")
        if tag == "meta":
            if (a.get("http-equiv") or "").lower() == "refresh":
                raise ValueError("entrypoint redirect")
            if (a.get("http-equiv") or "").lower() == "content-security-policy":
                if self.csp is not None:
                    raise ValueError("duplicate CSP")
                self.csp = a.get("content", "")
        if tag == "script":
            if a.get("type") != "module" or not a.get("src"):
                raise ValueError("unapproved executable script")
            rel = local_path(a["src"])
            if not rel.startswith("assets/") or not rel.endswith(".js"):
                raise ValueError("unapproved module")
            self.resources.add(rel)
            self.modules += 1
            self.in_script = True
        if tag == "link":
            rel = a.get("rel")
            if rel not in {"stylesheet", "icon", "manifest"}:
                raise ValueError("unexpected link / prefetch")
            path = local_path(a.get("href"))
            if rel == "stylesheet":
                if not path.startswith("assets/") or not path.endswith(".css"):
                    raise ValueError("unapproved stylesheet")
                self.styles += 1
            elif rel == "icon":
                if not path.startswith("assets/") or not path.endswith((".png", ".ico", ".svg")):
                    raise ValueError("unapproved icon")
            else:
                if path != "manifest.webmanifest":
                    raise ValueError("unexpected web manifest")
                self.manifests += 1
            self.resources.add(path)

    def handle_endtag(self, tag):
        if tag == "script":
            self.in_script = False

    def handle_data(self, data):
        if self.in_script and data.strip():
            raise ValueError("inline JavaScript body")


def verify(dist: Path, surface: str) -> dict:
    if surface not in SURFACES:
        raise ValueError("unrecognized connected panel")
    files = bundle_files(dist)
    entry = Entry()
    entry.feed(files["index.html"].read_text(encoding="utf-8"))
    entry.close()
    if (not entry.lang_ok or not csp_is_same_origin(entry.csp or "")
            or entry.modules != 1 or entry.styles < 1
            or not entry.resources.issubset(files)):
        raise ValueError("unverified connected entrypoint")
    if surface == "member":
        if entry.manifests != 1 or "manifest.webmanifest" not in files:
            raise ValueError("member manifest missing")
    elif entry.manifests:
        raise ValueError("unreviewed center manifest")

    if "manifest.webmanifest" in files:
        manifest = json.loads(files["manifest.webmanifest"].read_text(encoding="utf-8"),
                              object_pairs_hook=unique)
        if (not isinstance(manifest, dict)
                or manifest.get("scope") != "/"
                or manifest.get("display") != "standalone"
                or not local_path(manifest.get("start_url", "")).startswith("patient-app/")
                or not isinstance(manifest.get("icons"), list)
                or not manifest["icons"]):
            raise ValueError("invalid same-origin app manifest")
        for icon in manifest["icons"]:
            if (not isinstance(icon, dict) or icon.get("type") != "image/png"
                    or local_path(icon.get("src")) not in files):
                raise ValueError("unreviewed app icon")

    for rel, path in files.items():
        if path.suffix.lower() == ".css":
            css = path.read_text(encoding="utf-8")
            if re.search(r"(?i)@import\b", css):
                raise ValueError("stylesheet import forbidden")
            for match in CSS_URL.finditer(css):
                resource = match.group(1).strip().strip("'\"")
                if resource.startswith(("data:image/svg+xml,", "data:image/svg+xml;base64,",
                                        "data:image/png;base64,")):
                    if len(resource) > 32768:
                        raise ValueError("unbounded inline CSS icon")
                elif local_path(resource) not in files:
                    raise ValueError("missing / remote CSS resource")
        if path.suffix.lower() == ".js":
            if re.search(r"(?i)sourceMappingURL\s*=", path.read_text(encoding="utf-8")):
                raise ValueError("source-map disclosure")

    entries = [{"path": rel, "bytes": path.stat().st_size,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
               for rel, path in sorted(files.items())]
    body = {"schema_version": 1, "surface": surface,
            "source_mode": "PROTECTED_SAME_ORIGIN_TEST_ONLY", "files": entries}
    digest = hashlib.sha256(json.dumps(body, sort_keys=True, separators=(",", ":"),
                                       ensure_ascii=False).encode("utf-8")).hexdigest()
    return {**body, "bundle_sha256": digest, "release_allowed": False}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--surface", required=True, choices=sorted(SURFACES))
    parser.add_argument("--dist", type=Path, required=True)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--output", type=Path)
    group.add_argument("--verify", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.dist.is_symlink():
            raise ValueError("linked distribution root")
        dist = args.dist.resolve(strict=True)
        report = args.output or args.verify
        if report.resolve().is_relative_to(dist):
            raise ValueError("manifest written inside distribution")
        value = verify(dist, args.surface)
        if args.verify:
            before = json.loads(args.verify.read_text(encoding="utf-8"),
                                object_pairs_hook=unique)
            if before != value:
                raise ValueError("compiled bytes diverged")
        else:
            report.parent.mkdir(parents=True, exist_ok=True)
            report.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n",
                              encoding="utf-8")
    except (OSError, ValueError, TypeError, UnicodeError, json.JSONDecodeError) as exc:
        print("CONNECTED PANEL BUNDLE DENIED: " + type(exc).__name__, file=sys.stderr)
        return 1
    print("CONNECTED PANEL BUNDLE VERIFIED: " + args.surface
          + "; release denied; no real user or clinical authority attested")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
