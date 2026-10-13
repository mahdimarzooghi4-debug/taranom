#!/usr/bin/env python3
"""TRN-TECH-101: fail-closed census of actually invoked Python CI test files.

A 14/14 green workflow is insufficient if a newly committed test_*.py was
silently omitted from a manually maintained runner. Offline bounded source
validation only: not runtime coverage, an independent review, or release.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import re
import sys

WORKFLOW = Path(".github/workflows/taranom-full-regression-reusable.yml")
GROUPS = {
    "policy": ("tests/policy_checks", "internal-contract-tests"),
    "ops": ("tests/ops_status", "real-local-ops-http-react"),
    "center": ("tests/center_demo", "ephemeral-center-workflow-full-stack"),
}
MAX_WORKFLOW_BYTES = 128 * 1024
MAX_TEST_FILES = 128
JOB = re.compile(r"^  ([a-z][a-z0-9_-]+):$")
DISCOVER = re.compile(
    r"python3 -m unittest discover -s tests/policy_checks "
    r"-p '(test_[a-z0-9_]+\.py)' -v"
)
DIRECT = re.compile(
    r"python3 (?:\.\./\.\./)?tests/(policy_checks|ops_status|center_demo)/"
    r"(test_[a-z0-9_]+\.py)"
)
LOCAL_CSP = "test_local_portal_csp.py"

def _observed_names(root: Path, folder: str) -> set[str]:
    directory = root / folder
    if directory.is_symlink() or not directory.is_dir():
        raise ValueError("TEST_DIRECTORY_UNAVAILABLE")
    files = list(directory.iterdir())
    if len(files) > MAX_TEST_FILES * 2:
        raise ValueError("TEST_CORPUS_TOO_LARGE")
    result = set()
    for p in files:
        if p.name.startswith("test_") and p.suffix == ".py":
            if p.is_symlink() or not p.is_file() or not re.fullmatch(
                r"test_[a-z0-9_]+\.py", p.name
            ):
                raise ValueError("UNSAFE_TEST_FILE")
            result.add(p.name)
    if not result or len(result) > MAX_TEST_FILES:
        raise ValueError("TEST_CORPUS_UNAVAILABLE")
    return result

def inspect(root: Path) -> dict:
    report = {
        "schema_version": 1,
        "scope": "CANONICAL_FULL_CI_PYTHON_TEST_INVOCATION_CENSUS",
        "status": "BLOCKED",
        "release_allowed": False,
        "security_clearance": False,
        "runtime_coverage_certified": False,
        "counts": {},
        "findings": [],
    }
    problems = set()
    try:
        path = root / WORKFLOW
        if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_WORKFLOW_BYTES:
            raise ValueError("CANONICAL_WORKFLOW_UNAVAILABLE")
        lines = path.read_text(encoding="utf-8").splitlines()
        if not lines or len(lines) > 1500:
            raise ValueError("CANONICAL_WORKFLOW_UNAVAILABLE")
        current = None
        invoked = {kind: [] for kind in GROUPS}
        job_counts = Counter()
        for raw in lines:
            match = JOB.fullmatch(raw)
            if match:
                current = match.group(1)
                job_counts[current] += 1
                continue
            # Reject a script hidden in a comment/echo or a command with
            # shell transforms, suppression, redirection or extra arguments:
            # only the complete trimmed literal command can count.
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            discovered = DISCOVER.fullmatch(line)
            direct = DIRECT.fullmatch(line)
            if discovered:
                kind, name = "policy", discovered.group(1)
            elif direct:
                kind = {
                    "policy_checks": "policy", "ops_status": "ops",
                    "center_demo": "center",
                }[direct.group(1)]
                name = direct.group(2)
            else:
                # A modified command containing a test path or discovery
                # must not be accepted as executed coverage.
                if ("tests/policy_checks/test_" in line or
                        "tests/ops_status/test_" in line or
                        "tests/center_demo/test_" in line or
                        "discover -s tests/policy_checks" in line):
                    problems.add("NONCANONICAL_TEST_INVOCATION")
                continue
            expected_job = GROUPS[kind][1]
            # The local portal CSP test is intentionally invoked in the
            # ops build step after browser app artifacts are available.
            if kind == "policy" and name == LOCAL_CSP and current == "real-local-ops-http-react":
                pass
            elif current != expected_job:
                problems.add("TEST_IN_WRONG_CI_JOB")
            invoked[kind].append(name)
        for kind, (folder, required_job) in GROUPS.items():
            if job_counts[required_job] != 1:
                problems.add("REQUIRED_TEST_JOB_MISSING_OR_DUPLICATE")
            actual = _observed_names(root, folder)
            commands = Counter(invoked[kind])
            missing = actual - commands.keys()
            unexpected = commands.keys() - actual
            duplicates = {name for name, count in commands.items() if count != 1}
            if missing:
                problems.add("COMMITTED_TEST_NOT_EXECUTED")
            if unexpected:
                problems.add("WORKFLOW_INVOCATION_HAS_NO_TEST_FILE")
            if duplicates:
                problems.add("DUPLICATE_TEST_EXECUTION")
            report["counts"][kind] = {
                "tracked_test_files": len(actual),
                "literal_invocations": sum(commands.values()),
                "not_invoked": len(missing),
                "duplicate_invocations": len(duplicates),
                "untracked_invocations": len(unexpected),
            }
        # Essential browser E2E suites must also be invoked at full scope,
        # not a single filtered test file. This is lexical, not execution proof.
        browser_jobs = {
            "real-local-ops-http-react": 1,
            "ephemeral-center-workflow-full-stack": 2,
        }
        for job, required in browser_jobs.items():
            # Re-extract the specific job block to avoid a false match from
            # unrelated steps or YAML comments.
            block = []
            in_job = False
            for raw in lines:
                m = JOB.fullmatch(raw)
                if m:
                    in_job = m.group(1) == job
                elif in_job:
                    block.append(raw.strip())
            if block.count("CI=true npm run test:e2e") != required:
                problems.add("FULL_BROWSER_TEST_INVOCATION_MISSING")
    except (OSError, UnicodeError, ValueError) as error:
        allowed = {
            "TEST_DIRECTORY_UNAVAILABLE", "TEST_CORPUS_TOO_LARGE",
            "UNSAFE_TEST_FILE", "TEST_CORPUS_UNAVAILABLE",
            "CANONICAL_WORKFLOW_UNAVAILABLE",
        }
        problems.add(str(error) if str(error) in allowed else "CENSUS_SOURCE_UNVERIFIABLE")
    report["findings"] = sorted(problems)
    if not problems and len(report["counts"]) == len(GROUPS):
        report["status"] = "CONSISTENT_SOURCE_ONLY"
    return report

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--report-json", type=Path, required=True)
    args = parser.parse_args(argv)
    report = inspect(args.root.resolve())
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(
        json.dumps(report, sort_keys=True, indent=2) + "\n", encoding="utf-8"
    )
    print(
        "Full CI Python test census: " + report["status"] +
        "; release_allowed=false; actual runtime coverage NOT certified"
    )
    for rule in report["findings"]:
        print("DENIED " + rule, file=sys.stderr)
    return 0 if not report["findings"] else 1

if __name__ == "__main__":
    raise SystemExit(main())
