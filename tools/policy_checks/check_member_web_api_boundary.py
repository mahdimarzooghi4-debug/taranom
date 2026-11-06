#!/usr/bin/env python3
"""Enforce the narrow, host-authenticated member API client boundary."""
from __future__ import annotations

import argparse
import re
from pathlib import Path

API_MARKERS = ("/api/v1/", "Bearer ")
ALLOWED_API_CLIENTS = {"appointment-client.ts", "taranom-api.ts"}
FORBIDDEN_STATE = re.compile(
    r"localStorage\s*\.\s*setItem|sessionStorage\s*\.\s*setItem|fake(balance|login|token)",
    re.IGNORECASE,
)
TOKEN_GATE = re.compile(r"if\s*\(\s*!\s*token\s*\)")
FETCH_OPTIONS = {
    "cache": re.compile(r"cache\s*:\s*['\"]no-store['\"]"),
    "credentials": re.compile(r"credentials\s*:\s*['\"]omit['\"]"),
    "redirect": re.compile(r"redirect\s*:\s*['\"]error['\"]"),
}
CLIENT_IDENTITY = {
    "appointment-client.ts": "access.getAccessToken()",
    "taranom-api.ts": "window.taranomSession?.getAccessToken()",
}
EXTERNAL_FETCH = re.compile(r"fetch\s*\(\s*['\"]https?://", re.IGNORECASE)


def inspect_source_tree(source_root: Path) -> list[str]:
    errors: list[str] = []
    if not source_root.is_dir():
        return [f"member source root is missing: {source_root}"]
    for forbidden in ("fixtures", "mocks", "auth-provider.ts"):
        if (source_root / forbidden).exists():
            errors.append(f"forbidden synthetic/auth adapter exists: {forbidden}")

    client_text: dict[str, str] = {}
    for path in sorted(source_root.rglob("*")):
        if not path.is_file():
            continue
        try:
            content = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        relative = path.relative_to(source_root).as_posix()
        if FORBIDDEN_STATE.search(content):
            errors.append(f"browser-persisted/fake member state found: {relative}")
        has_api = any(marker in content for marker in API_MARKERS)
        if has_api and relative not in ALLOWED_API_CLIENTS:
            errors.append(f"API route or bearer token outside approved client: {relative}")
        if relative in ALLOWED_API_CLIENTS:
            client_text[relative] = content
            if EXTERNAL_FETCH.search(content):
                errors.append(f"absolute external fetch is forbidden: {relative}")

    for name, content in client_text.items():
        if not any(marker in content for marker in API_MARKERS):
            continue
        if CLIENT_IDENTITY[name] not in content:
            errors.append(f"API client does not obtain identity from its host: {name}")
        if not TOKEN_GATE.search(content):
            errors.append(f"API client does not fail closed without a token: {name}")
        for option, pattern in FETCH_OPTIONS.items():
            if not pattern.search(content):
                errors.append(f"API client must enforce {option} boundary: {name}")
        if "Authorization" not in content:
            errors.append(f"API client does not attach authenticated authorization: {name}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=Path("src"))
    args = parser.parse_args()
    errors = inspect_source_tree(args.source_root)
    if errors:
        for error in errors:
            print(f"MEMBER API BOUNDARY DENIED: {error}")
        return 1
    print("MEMBER API BOUNDARY VERIFIED: API use is confined to host-authenticated clients.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
