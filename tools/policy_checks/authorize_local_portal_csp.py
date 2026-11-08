#!/usr/bin/env python3
"""Scoped HTML CSP for real localhost:4318 .NET-only Member/Center bundles.

Legacy disconnected bundles begin with connect-src 'none'. Protected panels
already specify connect-src 'self'. Only exact same-origin policy is accepted
in the ephemeral Actions wwwroot artifacts; no remote origins are admitted.
"""
import argparse
import os
from pathlib import Path
import sys

class AdmissionError(Exception): pass

def authorize(html: str, prefix: str) -> str:
    # The original disconnected bundle is permitted to gain loopback-only
    # connectivity. Newer protected panel bundles already declare same-origin
    # connectivity: retain that exact policy rather than rejecting the build.
    # Neither form is allowed to grant remote, wildcard or duplicate connect.
    none = html.count("connect-src 'none'")
    self_only = html.count("connect-src 'self'")
    if (not isinstance(html, str) or not 0 < len(html) < 131072
        or html.count('<meta http-equiv="Content-Security-Policy"') != 1
        or none + self_only != 1
        or html.count("connect-src ") != 1
        or prefix + "/assets/" not in html
        or "default-src 'none'" not in html
        or "script-src 'self'" not in html
        or "form-action 'none'" not in html
        or "base-uri 'none'" not in html):
        raise AdmissionError("LOOPBACK_CSP_INPUT_UNVERIFIABLE")
    return html if self_only else html.replace("connect-src 'none'","connect-src 'self'",1)

def main(argv=None):
    parser=argparse.ArgumentParser()
    parser.add_argument("--audience",choices=("member","center"),required=True)
    args=parser.parse_args(argv)
    if os.getenv("GITHUB_ACTIONS")!="true" or os.getenv("CI")!="true":
        raise AdmissionError("EPHEMERAL_ACTIONS_ONLY")
    root=Path(__file__).resolve().parents[2]
    prefix="/member-portal" if args.audience=="member" else "/center-portal"
    file=root/"src/Taranom.OperationsStatus/wwwroot"/prefix.lstrip("/")/"index.html"
    if file.is_symlink() or not file.is_file() or file.stat().st_size > 131072:
        raise AdmissionError("BOUND_COMPILED_HTML_REQUIRED")
    original=file.read_text(encoding="utf-8")
    updated=authorize(original,prefix)
    file.write_text(updated,encoding="utf-8")
    if file.read_text(encoding="utf-8") != updated:
        raise AdmissionError("CSP_WRITE_UNVERIFIABLE")
    print("PASS: scoped same-origin CSP for ephemeral "+args.audience+" bundle")

if __name__=="__main__":
    try:raise SystemExit(main())
    except (AdmissionError,OSError,UnicodeError) as error:
        print("DENIED: "+(str(error) if isinstance(error,AdmissionError)
                          else "CSP_IO_UNVERIFIABLE"),file=sys.stderr)
        raise SystemExit(1)
