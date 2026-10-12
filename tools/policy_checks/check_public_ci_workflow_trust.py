#!/usr/bin/env python3
"""TRN-TECH-066: bounded static GitHub Actions trust fence for public CI.

This is a lexer/invariant checker, NOT a full YAML interpreter, code security
audit, run attestation, deployment approval or release admission.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

WORKFLOW_DIR = Path(".github/workflows")
# These two special source files are the entire local reusable-workflow trust
# envelope; all historical workflows retain their original strict step rules.
REUSABLE_NAME = "taranom-full-regression-reusable.yml"
DISPATCHER_NAME = "sprint-71-shared-secure-ci-gates.yml"
REUSABLE_SOURCE = "./.github/workflows/" + REUSABLE_NAME
REQUIRED_FULL_JOBS = frozenset({
    "internal-contract-tests",
    "synthetic-sprint01-regression",
    "member-web-sprint02-regression",
    "isolated-proofing-postgres",
    "internal-clinical-center-governance",
    "center-web-browser-tests",
    "isolated-center-postgres",
    "center-authority-negative-tests",
    "protected-center-interlock-tests",
    "protected-center-interlock-postgres",
    "evidence-review-intake-tests",
    "isolated-review-evidence-postgres",
    "real-local-ops-http-react",
    "ephemeral-center-workflow-full-stack",
})
MAX_WORKFLOW_BYTES = 128 * 1024
ALLOWED_TRIGGERS = frozenset({"push", "pull_request", "workflow_dispatch", "workflow_call"})
APPROVED_ACTIONS = frozenset({
    "actions/checkout", "actions/setup-dotnet", "actions/setup-node",
    "actions/upload-artifact",
})
FULL_PIN = re.compile(r"([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)@([0-9a-f]{40})\Z")
ACTION_STEP = re.compile(r"^(\s*)-\s+uses:\s+(\S+)(?:\s+#.*)?$")
ROOT_KEY = re.compile(r"^([A-Za-z_-][A-Za-z0-9_-]*):\s*(?:#.*)?$")
TRIGGER_KEY = re.compile(r"^  ([a-z_]+):(?:\s*.*)?$")
JOB_KEY = re.compile(r"^  ([a-z][a-z0-9_-]+):(?:\s*#.*)?$")
UNSAFE_EVENT = re.compile(r"^\s*(?:pull_request_target|workflow_run|repository_dispatch|schedule|release|deployment|deployment_status|issue_comment):")
# Sensitive use expressions and token emission are outside the intended
# no-secrets synthetic CI boundary. This is deliberately conservative.
SECRET_EXPR = re.compile(r"\$\{\{\s*(?:secrets|vars)\.", re.I)
WRITE_TOKEN = re.compile(r"\b(?:id-token|attestations|deployments|packages|actions|checks|issues|pull-requests|statuses|security-events):\s*write\b", re.I)


def _issue(results: list[dict], path: str, rule: str) -> None:
    results.append({"file": path, "rule": rule})


def inspect_workflow(path: Path) -> list[str]:
    """Inspect only first-party source text, never evaluate workflow scripts."""
    if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_WORKFLOW_BYTES:
        return ["UNREADABLE_OR_SYMLINKED_WORKFLOW"]
    try:
        raw = path.read_bytes()
        body = raw.decode("utf-8")
    except (OSError, UnicodeError):
        return ["UNREADABLE_OR_SYMLINKED_WORKFLOW"]
    if not body or "\t" in body or "\x00" in body:
        return ["INVALID_WORKFLOW_TEXT"]
    lines = body.splitlines()
    # Skip full-line comments, not executable lines with trailing comments.
    active = [line for line in lines if line.strip() and not line.lstrip().startswith("#")]
    violations: set[str] = set()
    roots = [(i, ROOT_KEY.fullmatch(s).group(1))
             for i, s in enumerate(active) if ROOT_KEY.fullmatch(s)]
    root_positions = {}
    for i, key in roots:
        root_positions.setdefault(key, []).append(i)
    for key in ("on", "permissions", "jobs"):
        if len(root_positions.get(key, [])) != 1:
            violations.add("MISSING_OR_DUPLICATE_REQUIRED_ROOT")
    if violations:
        return sorted(violations)
    on_pos, permissions_pos, jobs_pos = (root_positions[k][0]
                                        for k in ("on", "permissions", "jobs"))
    if not (on_pos < permissions_pos < jobs_pos):
        violations.add("UNEXPECTED_WORKFLOW_ROOT_ORDER")
    if not active[on_pos].startswith("on:") or not active[permissions_pos].startswith("permissions:"):
        violations.add("UNSUPPORTED_ROOT_VALUE")
    trigger_lines = active[on_pos + 1:permissions_pos] if on_pos < permissions_pos else []
    triggers = {m.group(1) for s in trigger_lines if (m := TRIGGER_KEY.match(s))}
    if not triggers or not triggers <= ALLOWED_TRIGGERS:
        violations.add("UNSAFE_OR_UNKNOWN_WORKFLOW_EVENT")
    if "workflow_call" in triggers and (
            path.name != REUSABLE_NAME or triggers != {"workflow_call"}):
        violations.add("UNAUTHORIZED_WORKFLOW_CALL")
    if path.name == REUSABLE_NAME and triggers != {"workflow_call"}:
        violations.add("REUSABLE_TRIGGER_NOT_ISOLATED")
    if path.name == DISPATCHER_NAME and triggers != {
            "push", "pull_request", "workflow_dispatch"}:
        violations.add("DISPATCHER_TRIGGERS_CHANGED")
    if any(UNSAFE_EVENT.match(s) for s in active):
        violations.add("PRIVILEGED_OR_UNSAFE_WORKFLOW_EVENT")

    perm_lines = active[permissions_pos + 1:jobs_pos] if permissions_pos < jobs_pos else []
    if [x.strip() for x in perm_lines] != ["contents: read"]:
        violations.add("WORKFLOW_TOKEN_NOT_READ_ONLY")
    if sum(bool(re.match(r"^\s*permissions:", s)) for s in active) != 1:
        violations.add("JOB_PERMISSION_OVERRIDE")
    if any(WRITE_TOKEN.search(s) for s in active):
        violations.add("WRITE_TOKEN_SCOPE")
    if any(SECRET_EXPR.search(s) for s in active):
        violations.add("SECRET_EXPRESSION_FORBIDDEN")
    if any(re.match(r"^\s*(?:environment|concurrency|container):\s*", s) for s in active):
        violations.add("UNREVIEWED_ENVIRONMENT_OR_CONTAINER")
    if any(re.match(r"^\s*id-token:\s*", s) for s in active):
        violations.add("OIDC_TOKEN_FORBIDDEN")

    job_lines = active[jobs_pos + 1:]
    job_names = [m.group(1) for s in job_lines if (m := JOB_KEY.fullmatch(s))]
    # The only job-level uses is a literal local same-commit call in the
    # explicitly named Sprint71 dispatcher; no with/secrets/if/runner/steps.
    if path.name == DISPATCHER_NAME:
        if job_lines != [
            "  full-regression:",
            "    uses: " + REUSABLE_SOURCE,
        ]:
            violations.add("UNSAFE_OR_INVALID_REUSABLE_CALL")
        if any(re.match(r"^\s*(?:with|secrets|if|continue-on-error|environment):", s)
               for s in active):
            violations.add("REUSABLE_CALL_PRIVILEGE_OR_SKIP")
        return sorted(violations)
    if path.name == REUSABLE_NAME:
        if set(job_names) != REQUIRED_FULL_JOBS or len(job_names) != 14:
            violations.add("REQUIRED_FULL_CI_JOB_MISSING_OR_EXTRA")
        # An omitted job body / inserted conditional skip is never accepted.
        if any(re.match(r"^\s*(?:if|continue-on-error|secrets|with):", s)
               for s in job_lines):
            # with: is permitted solely inside an approved first-party pinned
            # action step; it is checked below, not prohibited universally.
            if any(re.match(r"^\s*(?:if|continue-on-error|secrets):", s)
                   for s in job_lines):
                violations.add("REUSABLE_JOB_SKIP_OR_SECRET_FORBIDDEN")
    else:
        # No non-dispatcher workflow may invoke a reusable job.
        if any(re.match(r"^    uses:", s) for s in job_lines):
            violations.add("UNAUTHORIZED_REUSABLE_JOB")
    job_count = len(job_names)
    runner_count = sum(bool(re.fullmatch(r"    runs-on:\s+ubuntu-latest(?:\s+#.*)?", s))
                       for s in job_lines)
    if job_count < 1 or runner_count != job_count:
        violations.add("UNAPPROVED_JOB_RUNNER")
    if not any(re.match(r"^\s*-\s*uses:", s) for s in job_lines):
        violations.add("MISSING_PINNED_ACTIONS")

    for i, line in enumerate(active):
        if re.match(r"^\s*-\s*uses:", line):
            m = ACTION_STEP.fullmatch(line)
            if m is None:
                violations.add("DYNAMIC_OR_INVALID_ACTION")
                continue
            token = m.group(2)
            match = FULL_PIN.fullmatch(token)
            if not match or match.group(1) not in APPROVED_ACTIONS:
                violations.add("UNPINNED_OR_UNAPPROVED_ACTION")
                continue
            if match.group(1) == "actions/checkout":
                # The exact checkout step is isolated by the next step marker,
                # not by an arbitrary text window (prevents distant false match).
                indent = len(m.group(1))
                end = i + 1
                while end < len(active):
                    next_line = active[end]
                    if re.match(r"^\s*-\s*(?:uses|name|run):", next_line) and (
                            len(next_line) - len(next_line.lstrip())) <= indent:
                        break
                    end += 1
                step = active[i + 1:end]
                if not any(re.fullmatch(r"\s+persist-credentials:\s+false(?:\s+#.*)?", x)
                           for x in step):
                    violations.add("CHECKOUT_PERSISTS_CREDENTIALS")
        elif re.match(r"^\s*uses:", line):
            # GitHub Actions also allows the action as a property of a
            # named step: '- name: Upload' followed by 'uses: ...'.
            # It must be anchored to that very step, never arbitrary YAML.
            indent = len(line) - len(line.lstrip())
            previous = None
            for prior in range(i - 1, -1, -1):
                check = active[prior]
                pindent = len(check) - len(check.lstrip())
                if pindent <= indent - 2 and re.match(
                        r"^\s*-\s*(?:name|uses|run):", check):
                    previous = check
                    break
            token_match = re.fullmatch(r"\s*uses:\s+(\S+)(?:\s+#.*)?", line)
            if previous is None or not re.match(r"^\s*-\s*name:", previous):
                violations.add("NONSTEP_OR_DYNAMIC_ACTION")
            elif token_match is None:
                violations.add("DYNAMIC_OR_INVALID_ACTION")
            else:
                pin = FULL_PIN.fullmatch(token_match.group(1))
                if pin is None or pin.group(1) not in APPROVED_ACTIONS:
                    violations.add("UNPINNED_OR_UNAPPROVED_ACTION")
                elif pin.group(1) == "actions/checkout":
                    # Do not permit a second checkout syntax to omit the
                    # same-step nonpersistent credential requirement.
                    boundary = i + 1
                    while boundary < len(active) and not (
                            re.match(r"^\s*-\s*(?:name|uses|run):", active[boundary])
                            and len(active[boundary]) - len(active[boundary].lstrip())
                            <= indent - 2):
                        boundary += 1
                    if not any(re.fullmatch(
                            r"\s+persist-credentials:\s+false(?:\s+#.*)?", value)
                            for value in active[i + 1:boundary]):
                        violations.add("CHECKOUT_PERSISTS_CREDENTIALS")
    return sorted(violations)


def scan(root: Path) -> dict:
    """Fail closed if the workflow tree itself is unavailable or ambiguous."""
    report = {"schema_version": 1, "scope": "TRACKED_PUBLIC_GITHUB_ACTIONS_SOURCE",
              "status": "BLOCKED", "release_allowed": False,
              "security_clearance": False, "workflow_count": 0,
              "findings": []}
    path = root / WORKFLOW_DIR
    if path.is_symlink() or not path.is_dir():
        _issue(report["findings"], ".github/workflows", "WORKFLOW_ROOT_UNAVAILABLE")
        return report
    try:
        children = sorted(path.iterdir())
        if not children:
            _issue(report["findings"], ".github/workflows", "NO_WORKFLOWS")
        for entry in children:
            rel = entry.relative_to(root).as_posix()
            if entry.is_symlink() or not entry.is_file() or entry.suffix not in (".yml", ".yaml"):
                _issue(report["findings"], rel, "UNEXPECTED_WORKFLOW_TREE_ENTRY")
                continue
            report["workflow_count"] += 1
            for rule in inspect_workflow(entry):
                _issue(report["findings"], rel, rule)
    except (OSError, ValueError):
        _issue(report["findings"], ".github/workflows", "WORKFLOW_TREE_UNVERIFIABLE")
    report["status"] = "PASS_LIMITED_STATIC_SCOPE" if (report["workflow_count"] and
                       not report["findings"]) else "BLOCKED"
    return report


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--report-json", type=Path, required=True)
    args = parser.parse_args(argv)
    result = scan(args.root.resolve())
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(result, ensure_ascii=True,
                                          indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Public CI workflow trust: {result['status']}; "
          f"workflows={result['workflow_count']}; findings={len(result['findings'])}; "
          "release_allowed=false; NOT a security or deployment certification.")
    if result["findings"]:
        for item in result["findings"]:
            print(f"DENIED {item['file']}: {item['rule']}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
