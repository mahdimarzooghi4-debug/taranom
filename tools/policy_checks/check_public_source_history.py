#!/usr/bin/env python3
"""TRN-TECH-063: cautious, offline public repository source/history guard.

Scans Git objects reachable from locally fetched refs; NOT GitHub-hosted secret
scanning, unreachable objects, screenshots, production keys, or legal clearance.
Never prints or persists captured values. A PASS is NOT a clean-history claim.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys

MAX_TEXT_SIZE = 2_000_000
MAX_FINDINGS = 50
SENSITIVE_NAMES = frozenset({
    '.env', '.npmrc', '.pypirc', '.netrc', 'id_rsa', 'id_ed25519',
    'credentials.json', 'secrets.json', 'terraform.tfstate',
})
SENSITIVE_SUFFIXES = ('.pem', '.p12', '.pfx', '.key', '.tfstate', '.tfstate.backup')
BINARY_SUFFIXES = ('.png', '.jpg', '.jpeg', '.gif', '.webp', '.ico',
                   '.woff', '.woff2', '.ttf', '.otf', '.pdf', '.zip',
                   '.gz', '.jar', '.dll', '.exe', '.mp4', '.mp3')
EXAMPLE_ENV = frozenset({'.env.example', '.env.sample', '.env.template'})

# Construct fixed signature fragments separately so the checker source itself
# cannot accidentally contain a credential-like example or trip its own scan.
PATTERNS = {
    'GITHUB_ACCESS_TOKEN': re.compile(rb'(?:gh[pousr]_[A-Za-z0-9_]{30,}|github_pat_[A-Za-z0-9_]{70,})'),
    'AWS_ACCESS_KEY_ID': re.compile(rb'AKIA[A-Z0-9]{16}'),
    'STRIPE_LIVE_SECRET_KEY': re.compile(rb'sk_live_[A-Za-z0-9]{16,}'),
    'SLACK_ACCESS_TOKEN': re.compile(rb'xox[baprs]-[0-9A-Za-z-]{24,}'),
    'OPENAI_STYLE_SECRET': re.compile(rb'sk-(?:proj-)?[A-Za-z0-9_-]{40,}'),
    'PRIVATE_KEY_PEM': re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----'),
}
ASSIGNMENT = re.compile(
    rb'(?im)^\s*(?:export\s+)?["\']?([A-Za-z][A-Za-z0-9_.-]{0,79})["\']?\s*[:=]\s*["\']?([A-Za-z0-9_+\-/=]{28,})["\']?\s*(?:[#;,}]|$)'
)
KEY_NAME = re.compile(rb'(?:^|[_\-.])(?:api[_-]?key|token|secret|password|private[_-]?key|client[_-]?secret)(?:$|[_\-.])', re.I)
META_NAME = re.compile(rb'(?:hash|digest|sha|fingerprint|revision)', re.I)


def _git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(['git', '-C', str(root), *args], stderr=subprocess.DEVNULL)


def is_sensitive_path(path: str) -> bool:
    """Check a Git object *path*; do not follow symlinks."""
    norm = path.replace('\\', '/').lower().removeprefix('./')
    parts = PurePosixPath(norm).parts
    name = parts[-1] if parts else ''
    if name in EXAMPLE_ENV:
        return False
    return (name in SENSITIVE_NAMES or
            name.startswith('.env.') or
            name.endswith(SENSITIVE_SUFFIXES) or
            (len(parts) >= 2 and parts[-2:] == ('.aws', 'credentials')))


def inspect_blob(path: str, data: bytes) -> list[str]:
    """Return RULE LABELS only; do not expose data or captured values."""
    found = []
    if is_sensitive_path(path):
        found.append('SENSITIVE_TRACKED_PATH')
    for rule, pattern in PATTERNS.items():
        if pattern.search(data):
            found.append(rule)
    for match in ASSIGNMENT.finditer(data):
        key, literal = match.group(1), match.group(2)
        # The project's long, plain lowercase synthetic PostgreSQL fixture
        # passwords occur only in isolated test source. Limit this exception
        # to exact Password property names and unmistakably synthetic shape.
        # Signature-based token patterns above always run, even in tests.
        synthetic_fixture = (path.startswith('tests/') and key == b'Password'
                             and re.fullmatch(rb'[a-z_]{28,80}', literal) is not None
                             and (b'synthetic' in literal or b'test' in literal)
                             and b'_' in literal)
        if (KEY_NAME.search(key) and not META_NAME.search(key)
                and not synthetic_fixture):
            found.append('LONG_LITERAL_CREDENTIAL_ASSIGNMENT')
            break
    return sorted(set(found))


def scan(root: Path, *, history: bool = True) -> dict:
    """Inspect all reachable fetched objects, not unreachable/dangling blobs."""
    result = {'schema_version': 1, 'scope': 'reachable_fetched_git_refs',
              'independent_credential_clearance': False,
              'national_release_allowed': False, 'status': 'BLOCKED',
              'git_objects_inspected': 0, 'text_blobs_checked': 0,
              'binary_blobs_skipped': 0, 'findings': []}
    try:
        if _git(root, 'rev-parse', '--is-inside-work-tree').strip() != b'true':
            raise ValueError('NOT_A_GIT_WORKTREE')
        if Path(_git(root, 'rev-parse', '--show-toplevel').decode().strip()).resolve() != root.resolve():
            raise ValueError('ROOT_MUST_BE_GIT_TOPLEVEL')
        if history and _git(root, 'rev-parse', '--is-shallow-repository').strip() != b'false':
            raise ValueError('SHALLOW_HISTORY_UNVERIFIABLE')
        raw = _git(root, 'rev-list', '--objects', '--all' if history else 'HEAD')
        lines = [(l[:40].decode('ascii'), l[41:].decode('utf8', 'replace'))
                 for l in raw.splitlines() if len(l) > 41 and l[40:41] == b' ']
        if not lines:
            raise ValueError('NO_REACHABLE_OBJECT_PATHS')
        # Git rev-list names one representative path for a blob. A historical
        # sensitive name can otherwise be missed when the exact same blob was
        # also committed at a harmless path. Inspect all logged path changes.
        # Git's default text path display quotes and escapes filenames with
        # newlines, quotes or non-ASCII bytes. NUL-delimited paths are exact.
        historical_paths = _git(root, 'log', '--all', '--format=',
                                '--name-only', '-z', '--no-renames')
        seen_paths = set()
        for old_path_bytes in historical_paths.split(b'\x00'):
            old_path = old_path_bytes.decode('utf8', 'surrogateescape')
            if old_path and old_path not in seen_paths:
                seen_paths.add(old_path)
                if is_sensitive_path(old_path):
                    _record(result, old_path, 'historic_path', 'HISTORICAL_SENSITIVE_PATH')
        # --batch is one long-lived local child; sending bounded groups avoids
        # stdin/stdout pipe backpressure and repeated fork overhead.
        with subprocess.Popen(['git', '-C', str(root), 'cat-file', '--batch'],
                              stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                              stderr=subprocess.DEVNULL) as proc:
            assert proc.stdin is not None and proc.stdout is not None
            for offset in range(0, len(lines), 64):
                segment = lines[offset:offset + 64]
                proc.stdin.write(b''.join(oid.encode() + b'\n' for oid, _ in segment))
                proc.stdin.flush()
                for oid, path in segment:
                    header = proc.stdout.readline().split()
                    if len(header) != 3 or header[0].decode() != oid:
                        raise ValueError('GIT_OBJECT_READ_FAILED')
                    kind = header[1].decode('ascii')
                    size = int(header[2])
                    if size < 0:
                        raise ValueError('INVALID_OBJECT_SIZE')
                    # Stream without loading oversized blobs; reject oversized
                    # likely-text blobs rather than silently certify them.
                    if size > MAX_TEXT_SIZE:
                        for n in range(size, 0, -65536):
                            if not proc.stdout.read(min(n, 65536)):
                                raise ValueError('TRUNCATED_GIT_OBJECT')
                        data = b''
                    else:
                        data = proc.stdout.read(size)
                        if len(data) != size:
                            raise ValueError('TRUNCATED_GIT_OBJECT')
                    if proc.stdout.read(1) != b'\n':
                        raise ValueError('BAD_GIT_OBJECT_DELIMITER')
                    if kind != 'blob':
                        continue
                    result['git_objects_inspected'] += 1
                    is_binary_path = path.lower().endswith(BINARY_SUFFIXES)
                    if size > MAX_TEXT_SIZE and not is_binary_path:
                        _record(result, path, oid, 'OVERSIZED_UNSCANNED_TEXT_OBJECT')
                    elif is_binary_path or (b'\0' in data):
                        result['binary_blobs_skipped'] += 1
                        if is_sensitive_path(path):
                            _record(result, path, oid, 'SENSITIVE_TRACKED_PATH')
                    else:
                        result['text_blobs_checked'] += 1
                        for rule in inspect_blob(path, data):
                            _record(result, path, oid, rule)
            proc.stdin.close()
            if proc.wait() != 0:
                raise ValueError('GIT_BATCH_FAILED')
    except (OSError, subprocess.CalledProcessError, ValueError, UnicodeError) as e:
        result['findings'].append({'rule': 'SCAN_UNVERIFIABLE',
                                   'detail': str(e) if isinstance(e, ValueError)
                                   else type(e).__name__})
    result['status'] = 'PASS_LIMITED_SCOPE' if not result['findings'] else 'BLOCKED'
    return result


def _record(result: dict, path: str, oid: str, rule: str) -> None:
    if len(result['findings']) < MAX_FINDINGS:
        result['findings'].append({'rule': rule, 'path': path, 'object_prefix': oid[:12]})
    elif len(result['findings']) == MAX_FINDINGS:
        result['findings'].append({'rule': 'MORE_FINDINGS_REDACTED'})


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path('.'))
    parser.add_argument('--history', action='store_true', required=True)
    parser.add_argument('--report-json', type=Path, required=True)
    args = parser.parse_args()
    report = scan(args.root.resolve(), history=args.history)
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(report, ensure_ascii=False,
                        sort_keys=True, indent=2) + '\n', encoding='utf-8')
    print(f"Public-history guard: {report['status']}; "
          f"objects={report['git_objects_inspected']}; "
          f"text={report['text_blobs_checked']}; "
          f"findings={len(report['findings'])}. "
          'No credentials are printed; limited reachable-ref scan is NOT legal clearance.')
    if report['status'] != 'PASS_LIMITED_SCOPE':
        for finding in report['findings']:
            print(f"DENIED {finding['rule']}: "
                  f"{json.dumps(finding.get('path', 'scan'), ensure_ascii=True)}",
                  file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
