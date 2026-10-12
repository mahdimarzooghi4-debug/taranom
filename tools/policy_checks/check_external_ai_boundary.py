#!/usr/bin/env python3
"""A fail-closed, dependency-free lexical tripwire for known external AI runtimes.

This gate is deliberately narrow. It does NOT prove network isolation, detect
obfuscated URLs, inspect transitive SDK behaviour, authorize local models or
replace runtime egress controls/security review. See TRN-TECH-043.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import os
from pathlib import Path
import re
import sys

EXECUTABLE_ROOTS = ("src", "apps", "backend", "ai", "services", "workers")
SKIP_DIRS = frozenset({
    ".git", ".venv", "venv", "node_modules", "dist", "build", "bin", "obj",
    ".next", ".vite", "coverage", "__pycache__", ".pytest_cache",
    ".mypy_cache", ".playwright", "playwright-report", "test-results",
})
SCAN_EXTENSIONS = frozenset({
    ".py", ".cs", ".fs", ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
    ".json", ".toml", ".yml", ".yaml", ".xml", ".sh", ".ps1", ".csproj",
    ".props", ".targets", ".config", ".env", ".txt",
})
SCAN_NAMES = frozenset({
    "Dockerfile", "Containerfile", "requirements", "requirements.txt",
    ".env", ".env.local", ".env.production", "Pipfile",
})

# Exact public runtime destinations, not provider/model names in explanatory
# documents. Do not reject local safetensors/torch/transformers workloads.
_PATTERNS = (
    ("external-ai-runtime-host", re.compile(
        r"(?i)(?:api\.openai\.com|api\.anthropic\.com|"
        r"generativelanguage\.googleapis\.com|api\.cohere\.ai|"
        r"api\.together\.xyz|api\.groq\.com|api\.fireworks\.ai|"
        r"api\.replicate\.com|api\.perplexity\.ai|"
        r"api\.mistral\.ai|api\.deepseek\.com|"
        r"bedrock-runtime\.[a-z0-9-]+\.amazonaws\.com|"
        r"[a-z0-9.-]+\.openai\.azure\.com)(?![a-z0-9.-])"
    )),
    ("external-ai-credential-or-endpoint", re.compile(
        r"(?i)\b(?:OPENAI_API_KEY|OPENAI_BASE_URL|"
        r"AZURE_OPENAI_API_KEY|AZURE_OPENAI_ENDPOINT|"
        r"ANTHROPIC_API_KEY|ANTHROPIC_BASE_URL|"
        r"COHERE_API_KEY|TOGETHER_API_KEY|GROQ_API_KEY|"
        r"FIREWORKS_API_KEY|REPLICATE_API_TOKEN|"
        r"MISTRAL_API_KEY|DEEPSEEK_API_KEY|"
        r"GOOGLE_GENERATIVE_AI_API_KEY|GOOGLE_GENAI_API_KEY)\b"
    )),
    ("external-ai-sdk-reference", re.compile(
        r"""(?ix)
        (?:
          ["'](?:@openai/[a-z0-9_./-]+|@anthropic-ai/sdk|
                @google/generative-ai|@google/genai|openai|anthropic|
                cohere-ai|cohere|groq-sdk|groq|replicate|together-ai|
                together|mistralai|@mistralai/mistralai|
                google-generativeai|google-genai)["']
          |
          \b(?:from\s+(?:openai|anthropic|cohere|groq|replicate|
              together|mistralai|google\.generativeai)\s+import|
              import\s+(?:openai|anthropic|cohere|groq|replicate|
              together|mistralai|google\.generativeai)\b)
          |
          \bfrom\s+google\s+import\s+(?:genai|generativeai)\b
        )
        """
    )),
    ("external-ai-python-dependency", re.compile(
        r"(?im)^\s*(?:openai|anthropic|cohere|cohere-ai|groq|groq-sdk|"
        r"together|together-ai|replicate|mistralai|google-generativeai|"
        r"google-genai)\s*(?:(?:[=<>~!]=?)|\[|\s*#|$)"
    )),
)


@dataclass(frozen=True, order=True)
class Finding:
    file: str
    line: int
    rule: str


def _is_scannable(path: Path) -> bool:
    return path.suffix.lower() in SCAN_EXTENSIONS or path.name in SCAN_NAMES


def scan_repository(root: Path) -> list[Finding]:
    """Scan first-party runnable trees, never silently follow symlinks."""
    root = root.resolve(strict=True)
    findings: list[Finding] = []
    visited_roots = 0
    files_read = 0

    for name in EXECUTABLE_ROOTS:
        directory = root / name
        if not directory.exists() and not directory.is_symlink():
            continue
        visited_roots += 1
        if directory.is_symlink() or not directory.is_dir():
            findings.append(Finding(name, 0, "invalid-or-symlink-runtime-root"))
            continue

        for current, subdirs, filenames in os.walk(directory, followlinks=False):
            current_path = Path(current)
            for subdir in sorted(subdirs):
                candidate = current_path / subdir
                if candidate.is_symlink():
                    findings.append(Finding(candidate.relative_to(root).as_posix(),
                                            0, "symlink-in-runtime-tree"))
            subdirs[:] = sorted(d for d in subdirs
                                if d not in SKIP_DIRS
                                and not (current_path / d).is_symlink())
            for filename in sorted(filenames):
                path = current_path / filename
                rel = path.relative_to(root).as_posix()
                if path.is_symlink():
                    findings.append(Finding(rel, 0, "symlink-in-runtime-tree"))
                    continue
                if not _is_scannable(path):
                    continue
                files_read += 1
                try:
                    contents = path.read_text(encoding="utf-8")
                except (UnicodeError, OSError):
                    findings.append(Finding(rel, 0, "unreadable-runtime-source"))
                    continue
                for rule, regex in _PATTERNS:
                    for match in regex.finditer(contents):
                        line = contents.count("\n", 0, match.start()) + 1
                        findings.append(Finding(rel, line, rule))

    if visited_roots == 0 or files_read == 0:
        findings.append(Finding(".", 0, "no-scannable-runtime-source"))
    return sorted(set(findings))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="TRN-BIZ-037 static external AI runtime deny gate"
    )
    parser.add_argument("--root", type=Path, default=Path("."))
    args = parser.parse_args(argv)
    try:
        findings = scan_repository(args.root)
    except OSError as error:
        print(f"External AI policy gate cannot scan root: {error}", file=sys.stderr)
        return 2
    if findings:
        for item in findings:
            print(f"DENIED {item.file}:{item.line} [{item.rule}]", file=sys.stderr)
        print(f"External AI policy gate FAILED ({len(findings)} finding(s)).",
              file=sys.stderr)
        return 1
    print("External AI policy gate PASS: checked known SDK, host and key patterns "
          "in first-party runtime source. This is NOT network isolation.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
