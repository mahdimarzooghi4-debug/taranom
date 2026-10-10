#!/usr/bin/env python3
"""Deterministic integrity + active-resource guard for DISCONNECTED web builds.

Only verifies local output bytes and active HTML/CSS resource references.
The resulting SHA-256 list is NOT signed, a supply-chain attestation,
a statement about JavaScript's dynamic behavior, or Production admission.
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
from typing import Any

SURFACES = frozenset({"member", "center"})
EXTENSIONS = frozenset({
    ".html", ".js", ".css", ".svg", ".png", ".jpg", ".jpeg", ".webp",
    ".gif", ".ico", ".woff", ".woff2",
})
ASSET_PATH = re.compile(r"\A/assets/[A-Za-z0-9_.-]+\Z")
DIGEST = re.compile(r"\A[a-f0-9]{64}\Z")
URL_FUNC = re.compile(r"(?i)\burl\(\s*([^)]*)\s*\)")
MAP_REFERENCE = re.compile(r"(?i)sourceMappingURL\s*=")


def _json_unique(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    out: dict[str, Any] = {}
    for key, value in pairs:
        if key in out:
            raise ValueError("duplicate manifest key")
        out[key] = value
    return out


def _canonical(obj: Any) -> bytes:
    return json.dumps(obj, sort_keys=True, separators=(",", ":"),
                      ensure_ascii=False, allow_nan=False).encode("utf-8")


def _local_asset(url: str) -> str:
    # No remote, protocol-relative, percent-encoded, query, hash, traversal,
    # data URL, inline code or unbounded same-origin application route.
    if not ASSET_PATH.fullmatch(url):
        raise ValueError("active resource reference is not a local Vite asset")
    return url.lstrip("/")


class _EntryHTML(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.resources: set[str] = set()
        self.modules = 0
        self.stylesheets = 0
        self._in_script = False
        self.csp: str | None = None
        self.html_lang = False

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if len(values) != len(attrs):
            raise ValueError("duplicate HTML attribute")
        if tag == "html":
            self.html_lang = values.get("lang") == "fa" and values.get("dir") == "rtl"
        if tag in {"base", "iframe", "frame", "object", "embed", "form"}:
            raise ValueError("active embedding or form element forbidden")
        if tag == "meta" and (values.get("http-equiv") or "").lower() == "refresh":
            raise ValueError("HTML redirect forbidden")
        if tag == "meta" and (values.get("http-equiv") or "").lower() == "content-security-policy":
            if self.csp is not None:
                raise ValueError("duplicate CSP meta")
            self.csp = values.get("content") or ""
        if tag == "script":
            if values.get("type") != "module" or "src" not in values:
                raise ValueError("inline or untyped script forbidden")
            self.resources.add(_local_asset(values["src"] or ""))
            self.modules += 1
            self._in_script = True
        if tag == "link":
            if values.get("rel") != "stylesheet":
                raise ValueError("unexpected stylesheet, preload or external link")
            self.resources.add(_local_asset(values.get("href") or ""))
            self.stylesheets += 1
        if tag in {"img", "audio", "video", "source"}:
            raise ValueError("unreviewed active media resource")
        if tag in {"image", "use"}:
            raise ValueError("unreviewed inline SVG resource")

    def handle_endtag(self, tag: str) -> None:
        if tag == "script":
            self._in_script = False

    def handle_data(self, data: str) -> None:
        if self._in_script and data.strip():
            raise ValueError("inline script content forbidden")


def _files(dist: Path) -> dict[str, Path]:
    if dist.is_symlink() or not dist.is_dir():
        raise ValueError("missing or symlinked built bundle directory")
    files: dict[str, Path] = {}
    for here, dirs, names in os.walk(dist, followlinks=False):
        home = Path(here)
        for subdir in dirs:
            if (home / subdir).is_symlink():
                raise ValueError("symlinked bundle directory")
        for name in names:
            path = home / name
            if path.is_symlink() or not path.is_file():
                raise ValueError("symlinked or nonregular bundle file")
            relative = path.relative_to(dist).as_posix()
            if path.suffix.lower() not in EXTENSIONS or relative.endswith(".map"):
                raise ValueError("unexpected file type including sourcemap")
            if relative in files:
                raise ValueError("duplicate bundle file")
            files[relative] = path
    if "index.html" not in files:
        raise ValueError("no bundle entrypoint")
    if len(files) < 3:
        raise ValueError("bundle must contain local HTML, JS and CSS")
    return files


def verify_bundle(dist: Path, surface: str) -> dict[str, Any]:
    if surface not in SURFACES:
        raise ValueError("unknown browser surface")
    files = _files(dist)
    parser = _EntryHTML()
    try:
        parser.feed(files["index.html"].read_text(encoding="utf-8"))
        parser.close()
    except UnicodeError as exc:
        raise ValueError("unreadable entry HTML") from exc
    if not parser.html_lang:
        raise ValueError("missing Persian RTL entry semantics")
    if parser.modules != 1 or parser.stylesheets < 1:
        raise ValueError("expected exactly one JS module and local CSS")
    csp = parser.csp or ""
    if not all(x in csp for x in (
        "default-src 'none'", "connect-src 'none'",
        "script-src 'self'", "form-action 'none'",
    )):
        raise ValueError("disconnected CSP meta missing")
    if not parser.resources or not parser.resources.issubset(files):
        raise ValueError("HTML references missing bundled asset")
    for asset in parser.resources:
        if asset.endswith(".js") or asset.endswith(".css"):
            continue
        raise ValueError("entry links non-JS/CSS active asset")
    if not any(x.endswith(".js") for x in parser.resources) or not any(
        x.endswith(".css") for x in parser.resources
    ):
        raise ValueError("expected self-hosted script and stylesheet")
    for rel, path in files.items():
        if path.suffix.lower() == ".css":
            try:
                stylesheet = path.read_text(encoding="utf-8")
            except UnicodeError as exc:
                raise ValueError("invalid CSS encoding") from exc
            if re.search(r"(?i)@import\b", stylesheet):
                raise ValueError("stylesheet import forbidden")
            for match in URL_FUNC.finditer(stylesheet):
                url = match.group(1).strip().strip("\"'")
                _local_asset(url)
                if url.lstrip("/") not in files:
                    raise ValueError("CSS references missing or remote asset")
        if path.suffix.lower() == ".js":
            # The known sourcemap suffix would otherwise disclose source files.
            # Do not inspect generic JS strings for URLs; React's diagnostic
            # messages may refer to docs without performing remote fetches.
            if MAP_REFERENCE.search(path.read_text(encoding="utf-8")):
                raise ValueError("JavaScript source map directive forbidden")

    entries = []
    for relative, path in sorted(files.items()):
        contents = path.read_bytes()
        entries.append({
            "path": relative,
            "bytes": len(contents),
            "sha256": hashlib.sha256(contents).hexdigest(),
        })
    body: dict[str, Any] = {
        "schema_version": 1,
        "surface": surface,
        "files": entries,
    }
    return {**body, "bundle_sha256": hashlib.sha256(_canonical(body)).hexdigest(),
            "release_allowed": False}


def _read_manifest(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as source:
        payload = json.load(source, object_pairs_hook=_json_unique)
    if not isinstance(payload, dict):
        raise ValueError("manifest must be an object")
    return payload


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Verify disconnected build bytes, not Production readiness")
    parser.add_argument("--dist", type=Path, required=True)
    parser.add_argument("--surface", choices=sorted(SURFACES), required=True)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--output", type=Path)
    group.add_argument("--verify", type=Path)
    args = parser.parse_args(argv)
    try:
        # Resolving a symlink would hide its identity from _files().
        # Deny a linked dist root *before* canonical path resolution.
        if args.dist.is_symlink():
            raise ValueError("symlinked bundle root")
        dist = args.dist.resolve(strict=True)
        report = args.output if args.output is not None else args.verify
        assert report is not None
        # Never include output evidence itself inside the assets being hashed.
        if report.resolve().is_relative_to(dist):
            raise ValueError("bundle manifest must stay outside dist")
        expected = verify_bundle(dist, args.surface)
        if args.verify is not None:
            if _read_manifest(args.verify) != expected:
                raise ValueError("bundle contents no longer match hashed inventory")
        else:
            report.parent.mkdir(parents=True, exist_ok=True)
            report.write_text(json.dumps(expected, ensure_ascii=False,
                                         indent=2) + "\n", encoding="utf-8")
    except (OSError, ValueError, UnicodeError) as exc:
        print(f"BUNDLE DENIED: {type(exc).__name__}", file=sys.stderr)
        return 1
    print(f"BUNDLE INTEGRITY VERIFIED: {args.surface}, {len(expected['files'])} files.")
    print("RELEASE DENIED: unhashed distribution paths, genuine deployment and approvals unverified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
