#!/usr/bin/env python3
"""Scoped HTML CSP for real localhost:4318 .NET-only Member/Center bundles.

Default standalone Member/Center HTML must keep connect-src 'none'. Only
the two already compiled, ephemeral Actions wwwroot artifacts may gain
connect-src 'self' to query the EXISTING anonymous denied-status endpoint.
"""
import argparse
import os
from pathlib import Path
import sys

class AdmissionError(Exception): pass

def authorize(html: str, prefix: str) -> str:
    if (not isinstance(html, str) or not 0 < len(html) < 131072
        or html.count('<meta http-equiv="Content-Security-Policy"') != 1
        or html.count("connect-src 'none'") != 1
        or "connect-src 'self'" in html
        or prefix + "/assets/" not in html
        or "script-src 'self'" not in html
        or "form-action 'none'" not in html
        or "base-uri 'none'" not in html):
        raise AdmissionError("LOOPBACK_CSP_INPUT_UNVERIFIABLE")
    return html.replace("connect-src 'none'","connect-src 'self'",1)

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
