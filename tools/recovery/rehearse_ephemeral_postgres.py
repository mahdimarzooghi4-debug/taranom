#!/usr/bin/env python3
"""TRN-TECH-086: destructive-test-free recovery rehearsal for ephemeral PG only.

The backup stays in a private temporary directory in CI and is never
uploaded. Restores only into a freshly created disposable second database.
Never usable with Production host, Production credentials, or real people.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from verify_postgres_security import (
    collect_security, attest_parity, SecurityMismatch,
)

SOURCE = "taranom_sprint64_center"
DESTINATION = "taranom_sprint77_restore"
EXPECTED_CONNECTION = (
    "Host=localhost;Port=5432;Database=" + SOURCE +
    ";Username=postgres;Password=only_synthetic_ci;Pooling=false"
)
TABLE_NAME = re.compile(r"internal_test_[a-z_]{1,64}\Z")
MANDATORY_TABLES = frozenset({
    "internal_test_center", "internal_test_center_event",
    "internal_test_center_audit", "internal_test_center_denial",
    "internal_test_support_case", "internal_test_support_event",
    "internal_test_support_audit",
})
MAX_DUMP_SIZE = 32 * 1024 * 1024
MAX_QUERY_SIZE = 16 * 1024 * 1024


class RecoveryError(Exception):
    pass


def _deny(reason: str):
    raise RecoveryError(reason)


def environment_allowed(env):
    return (
        env.get("GITHUB_ACTIONS") == "true"
        and env.get("TARANOM_CENTER_DEMO_TEST_ONLY") == "CI_EPHEMERAL_ONLY"
        and env.get("TARANOM_TEST_PG") == EXPECTED_CONNECTION
        and env.get("CI") == "true"
    )


def canonical_rows(lines: bytes):
    """Identical PostgreSQL COPY row contents, ignoring physical row order."""
    if len(lines) > MAX_QUERY_SIZE:
        _deny("RECOVERY_ROW_EVIDENCE_TOO_LARGE")
    try:
        values = [
            json.dumps(json.loads(line), sort_keys=True, separators=(",", ":"),
                       ensure_ascii=True) for line in lines.splitlines()
            if line
        ]
    except (ValueError, UnicodeDecodeError, TypeError, RecursionError):
        _deny("RECOVERY_ROW_EVIDENCE_UNPARSABLE")
    return len(values), hashlib.sha256(
        ("\n".join(sorted(values)) + "\n").encode("ascii")
    ).hexdigest()


def validate_tables(names):
    if (not names or len(names) > 50 or len(names) != len(set(names))
            or not MANDATORY_TABLES <= set(names)
            or any(not TABLE_NAME.fullmatch(n) for n in names)):
        _deny("NON_SYNTHETIC_OR_INCOMPLETE_RECOVERY_SCHEMA")
    return tuple(sorted(names))


def run_checked(argv, *, input_file=None, output_file=None, expect_failure=False):
    """Never run shell, echo credentials or reveal database payload on failure."""
    try:
        completed = subprocess.run(
            argv, stdin=input_file, stdout=output_file or subprocess.PIPE,
            stderr=subprocess.DEVNULL, timeout=100, check=False
        )
    except (OSError, subprocess.TimeoutExpired):
        _deny("RECOVERY_SUBPROCESS_UNAVAILABLE")
    if expect_failure:
        if completed.returncode == 0:
            _deny("RESTORED_APPEND_ONLY_GUARD_NOT_ENFORCED")
    elif completed.returncode != 0:
        _deny("RECOVERY_OPERATION_FAILED")
    return completed.stdout if output_file is None else None


def _docker_container():
    ids = run_checked([
        "docker", "ps", "-q", "--filter", "ancestor=postgres:18.6",
        "--filter", "publish=5432"
    ]).decode("ascii", errors="replace").split()
    if len(ids) != 1 or not re.fullmatch(r"[0-9a-f]{12,64}", ids[0]):
        _deny("ISOLATED_TEST_POSTGRES_CONTAINER_NOT_UNIQUE")
    return ids[0]


def _query(container, database, query):
    if database not in (SOURCE, DESTINATION):
        _deny("NON_EPHEMERAL_DATABASE_FORBIDDEN")
    raw = run_checked([
        "docker", "exec", container, "psql", "-X", "-q", "-t", "-A",
        "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d", database, "-c",
        query
    ])
    if len(raw) > MAX_QUERY_SIZE:
        _deny("RECOVERY_SQL_OUTPUT_TOO_LARGE")
    return raw


def _catalog(container, database):
    names = _query(container, database,
        "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;"
    ).decode("ascii", errors="strict").split()
    names = validate_tables(names)
    schema_queries = (
        """SELECT coalesce(jsonb_agg(jsonb_build_object(
               'table',c.relname,'col',a.attname,
               'type',format_type(a.atttypid,a.atttypmod),
               'required',a.attnotnull) ORDER BY c.relname,a.attnum)::text,'[]')
           FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
           JOIN pg_attribute a ON a.attrelid=c.oid
           WHERE n.nspname='public' AND c.relkind='r'
             AND a.attnum>0 AND NOT a.attisdropped;""",
        """SELECT coalesce(jsonb_agg(pg_get_triggerdef(t.oid) ORDER BY c.relname,t.tgname)::text,'[]')
           FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
           JOIN pg_namespace n ON n.oid=c.relnamespace
           WHERE n.nspname='public' AND NOT t.tgisinternal;""",
        """SELECT coalesce(jsonb_agg(pg_get_indexdef(i.indexrelid) ORDER BY c.relname,idx.relname)::text,'[]')
           FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid
           JOIN pg_class idx ON idx.oid=i.indexrelid
           JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public';""",
        """SELECT coalesce(jsonb_agg(pg_get_constraintdef(k.oid) ORDER BY c.relname,k.conname)::text,'[]')
           FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid
           JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public';""",
    )
    schema = tuple(json.loads(_query(container, database, q)) for q in schema_queries)
    triggers = json.dumps(schema[1])
    for required in ("internal_test_support_event_guard",
                     "internal_test_support_audit_guard",
                     "internal_test_center_event_guard",
                     "internal_test_center_audit_guard"):
        if required not in triggers:
            _deny("RECOVERY_APPEND_ONLY_TRIGGER_MISSING")
    rows = {}
    for table in names:
        # No untrusted interpolation: table names are strict internal_test_*.
        # SELECT outputs JSON text directly. COPY text would double-escape
        # command_json backslashes, corrupting valid nested event JSON.
        sql = 'SELECT row_to_json(t)::text FROM public."' + table + '" t;'
        rows[table] = canonical_rows(_query(container, database, sql))
    for table in ("internal_test_support_event", "internal_test_center_event"):
        if rows[table][0] < 1:
            _deny("RECOVERY_NO_REAL_SYNTHETIC_JOURNAL_EVENTS")
    for kind in ("support", "center"):
        rev = "revision" if kind == "support" else "event_version"
        col = "case_ref" if kind == "support" else "center_ref"
        mismatch = _query(container, database,
            "SELECT COUNT(*) FROM internal_test_" + kind + "_event e "
            "LEFT JOIN internal_test_" + kind + "_audit a ON "
            "e." + col + "=a." + col + " AND e." + rev + "=a." + rev + " "
            "WHERE a.event_digest IS DISTINCT FROM e.event_digest "
            "OR a.request_ref IS DISTINCT FROM e.request_ref;"
        ).decode("ascii").strip()
        if mismatch != "0":
            _deny("RECOVERY_EVENT_AUDIT_LINK_MISMATCH")
    # Capture and independently evaluate ACL/RLS/functions/effective rights
    # on BOTH source and restore. Rows and schema alone are not enough.
    try:
        security = collect_security(
            lambda sql: _query(container, database, sql))
    except SecurityMismatch as exc:
        _deny(str(exc))
    return names, schema, rows, security


def _guard_test(container):
    for table in ("internal_test_support_event", "internal_test_center_event"):
        sql = "UPDATE " + table + " SET event_digest=event_digest;"
        run_checked([
            "docker", "exec", container, "psql", "-X", "-q", "-t", "-A",
            "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d",
            DESTINATION, "-c", sql
        ], expect_failure=True)


def rehearse():
    if not environment_allowed(os.environ):
        _deny("EPHEMERAL_TEST_ENVIRONMENT_REQUIRED")
    container = _docker_container()
    before = _catalog(container, SOURCE)
    with tempfile.TemporaryDirectory(prefix="taranom-ephemeral-recovery-") as work:
        path = Path(work) / "synthetic-only.pgcustom"
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, "wb") as dump_file:
            run_checked([
                "docker", "exec", container, "pg_dump", "-U", "postgres",
                "-d", SOURCE, "-Fc", "--no-owner"
            ], output_file=dump_file)
        if not 0 < path.stat().st_size <= MAX_DUMP_SIZE:
            _deny("RECOVERY_BACKUP_SIZE_UNVERIFIABLE")
        run_checked([
            "docker", "exec", container, "createdb", "-U", "postgres",
            "-T", "template0", DESTINATION
        ])
        with path.open("rb") as dump_file:
            run_checked([
                "docker", "exec", "-i", container, "pg_restore", "-U", "postgres",
                "-d", DESTINATION, "--no-owner", "--exit-on-error"
            ], input_file=dump_file)
        after = _catalog(container, DESTINATION)
        if before[0] != after[0]:
            _deny("RECOVERY_RESTORED_TABLE_SET_DIFFERS")
        if before[1] != after[1]:
            _deny("RECOVERY_RESTORED_SCHEMA_DIFFERS")
        if before[2] != after[2]:
            _deny("RECOVERY_RESTORED_CONTENT_DIFFERS")
        try:
            attest_parity(before[3], after[3])
        except SecurityMismatch as exc:
            _deny(str(exc))
        _guard_test(container)
        if after != _catalog(container, DESTINATION):
            _deny("RECOVERY_GUARD_CHECK_CHANGED_RESTORED_BYTES")
    # Independent read of original confirms test rehearsal never mutated it.
    if before != _catalog(container, SOURCE):
        _deny("RECOVERY_ORIGINAL_DATABASE_CHANGED")
    return {"status": "EPHEMERAL_RECOVERY_REHEARSAL_PASSED",
            "tables": len(before[0]),
            "source_records": sum(v[0] for v in before[2].values()),
            "restored_records": sum(v[0] for v in after[2].values()),
            "synthetic_only": True,
            "production_recovery_verified": False,
            "rpo_rto_verified": False,
            "security_posture_equal": True,
            "stage4_recovery_verified": False,
            "national_release_allowed": False}


def main():
    try:
        result = rehearse()
    except RecoveryError as err:
        print("DENIED: " + str(err), file=sys.stderr)
        return 1
    except Exception:
        print("DENIED: EPHEMERAL_RECOVERY_UNVERIFIABLE", file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
