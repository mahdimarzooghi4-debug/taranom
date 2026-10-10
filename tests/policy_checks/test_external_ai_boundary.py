"""Adversarial tests for the bounded Sprint34 external-AI lexical gate."""
from __future__ import annotations

import os
from pathlib import Path
import tempfile
import unittest

from tools.policy_checks.check_external_ai_boundary import (
    Finding, main, scan_repository,
)


class ExternalAiBoundaryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="trn-ai-policy-")
        self.root = Path(self.temp.name)
        self.write("src/Taranom.MemberGovernance/Allowed.cs",
                   'namespace Taranom; // internal only\n')

    def tearDown(self) -> None:
        self.temp.cleanup()

    def write(self, path: str, contents: str) -> None:
        file = self.root / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(contents, encoding="utf-8")

    def issues(self) -> list[Finding]:
        return scan_repository(self.root)

    def test_allowed_internal_model_library_imports(self) -> None:
        self.write("ai/runtime/local.py",
                   "import torch\nfrom transformers import AutoModel\n"
                   "from safetensors.torch import load_file\n"
                   "model_ref = 'google/gemma-4-12B-it'\n")
        self.assertEqual([], self.issues())

    def test_legitimate_non_ai_provider_network_may_be_separately_governed(self) -> None:
        self.write("src/Taranom.Agreements/NonAi.cs",
                   'var idp = "https://identity.example.org/oidc";\n')
        self.assertEqual([], self.issues())

    def test_blocks_each_known_external_inference_host(self) -> None:
        hosts = [
            "api.openai.com", "api.anthropic.com",
            "generativelanguage.googleapis.com", "api.cohere.ai",
            "api.together.xyz", "api.groq.com", "api.fireworks.ai",
            "api.replicate.com", "api.perplexity.ai",
            "api.mistral.ai", "api.deepseek.com",
            "bedrock-runtime.us-east-1.amazonaws.com",
            "mytenant.openai.azure.com",
        ]
        for host in hosts:
            with self.subTest(host=host):
                self.write("apps/member-web/src/runtime.ts",
                           f'const endpoint = "https://{host}/v1";\n')
                self.assertTrue(any(f.rule == "external-ai-runtime-host"
                                    for f in self.issues()))

    def test_blocks_external_ai_key_material_references(self) -> None:
        variables = [
            "OPENAI_API_KEY", "OPENAI_BASE_URL", "AZURE_OPENAI_ENDPOINT",
            "ANTHROPIC_API_KEY", "COHERE_API_KEY", "TOGETHER_API_KEY",
            "GROQ_API_KEY", "FIREWORKS_API_KEY",
            "REPLICATE_API_TOKEN", "MISTRAL_API_KEY",
            "DEEPSEEK_API_KEY", "GOOGLE_GENAI_API_KEY",
        ]
        for variable in variables:
            with self.subTest(variable=variable):
                self.write("src/Config/Secrets.cs",
                           f'var fromEnvironment = "{variable}";\n')
                self.assertTrue(any(f.rule == "external-ai-credential-or-endpoint"
                                    for f in self.issues()))

    def test_blocks_npm_sdk_dependency_direct(self) -> None:
        self.write("apps/member-web/package.json",
                   '{"dependencies": {"@anthropic-ai/sdk": "1.0.0"}}')
        self.assertIn("external-ai-sdk-reference", [f.rule for f in self.issues()])

    def test_blocks_openai_package_lock_entries(self) -> None:
        self.write("apps/center-web/package-lock.json",
                   '{"packages": {"node_modules/openai": {"version": "5.0"}, '
                   '"root": {"dependencies": {"openai": "5.0"}}}}')
        self.assertIn("external-ai-sdk-reference", [f.rule for f in self.issues()])

    def test_blocks_google_cloud_generative_sdk_package(self) -> None:
        self.write("apps/member-web/package.json",
                   '{"dependencies": {"@google/generative-ai": "^0.1.0"}}')
        self.assertIn("external-ai-sdk-reference", [f.rule for f in self.issues()])

    def test_blocks_dotnet_external_ai_sdk_package(self) -> None:
        self.write("src/Example/Example.csproj",
                   '<PackageReference Include="OpenAI" Version="2.1.0" />')
        self.assertIn("external-ai-sdk-reference", [f.rule for f in self.issues()])

    def test_blocks_python_imports(self) -> None:
        statements = (
            "import openai", "from anthropic import Anthropic",
            "from google import genai", "import google.generativeai",
            "from mistralai import Mistral", "from groq import Groq",
        )
        for statement in statements:
            with self.subTest(statement=statement):
                self.write("ai/trainer/train.py", statement + "\n")
                self.assertIn("external-ai-sdk-reference",
                              [f.rule for f in self.issues()])

    def test_blocks_requirements_file_even_without_import(self) -> None:
        for dependency in ("openai==2.0", "anthropic>=0.1", "google-genai~=1.2",
                           "replicate", "mistralai[async]>=0.1"):
            with self.subTest(dependency=dependency):
                self.write("ai/requirements.txt", dependency + "\n")
                self.assertIn("external-ai-python-dependency",
                              [f.rule for f in self.issues()])

    def test_nested_new_first_party_source_root_is_scanned(self) -> None:
        self.write("workers/new-research/runner.py",
                   'URL = "https://api.cohere.ai/v2"\n')
        self.assertIn("external-ai-runtime-host", [f.rule for f in self.issues()])

    def test_hardcoded_secret_identifier_fails_even_without_hostname(self) -> None:
        self.write("src/App/Secrets.cs", 'var name = "AZURE_OPENAI_API_KEY";\n')
        self.assertIn("external-ai-credential-or-endpoint",
                      [f.rule for f in self.issues()])

    def test_ignores_documentation_and_test_fixtures(self) -> None:
        self.write("docs/reviews/Threats.md", "api.openai.com\n")
        self.write("tests/fixtures/external_runtime.txt",
                   "OPENAI_API_KEY\n")
        self.assertEqual([], self.issues())

    def test_ignores_generated_build_artifacts(self) -> None:
        self.write("apps/member-web/node_modules/bad/src/index.ts",
                   "import 'openai';")
        self.write("apps/member-web/dist/output.js",
                   '"api.openai.com"')
        self.write("src/Example/bin/Debug/generated.cs",
                   '"OPENAI_API_KEY"')
        self.assertEqual([], self.issues())

    def test_does_not_scan_images_or_lockfiles_outside_runnable_roots(self) -> None:
        self.write("apps/member-web/src/screenshot.png", "api.openai.com")
        self.write("package-lock.json", '{"dependencies":{"openai":"5.0"}}')
        self.assertEqual([], self.issues())

    def test_blocks_runtime_symlink_file_without_following_it(self) -> None:
        outside = self.root / "docs" / "safe.txt"
        outside.parent.mkdir(parents=True, exist_ok=True)
        outside.write_text("safe", encoding="utf-8")
        link = self.root / "src" / "evil.cs"
        link.symlink_to(outside)
        self.assertIn("symlink-in-runtime-tree",
                      [f.rule for f in self.issues()])

    def test_blocks_runtime_symlink_directory_without_following_it(self) -> None:
        source = self.root / "docs" / "source"
        source.mkdir(parents=True)
        (self.root / "apps").mkdir()
        (self.root / "apps" / "indirect").symlink_to(source, target_is_directory=True)
        self.assertIn("symlink-in-runtime-tree",
                      [f.rule for f in self.issues()])

    def test_blocks_missing_all_runtime_roots(self) -> None:
        with tempfile.TemporaryDirectory() as empty:
            issues = scan_repository(Path(empty))
        self.assertEqual(["no-scannable-runtime-source"], [f.rule for f in issues])

    def test_blocks_invalid_utf8_runtime_source(self) -> None:
        target = self.root / "src" / "Bad.cs"
        target.write_bytes(b"\xff\xfe\x00\x81")
        self.assertIn("unreadable-runtime-source", [f.rule for f in self.issues()])

    def test_output_never_exposes_secret_values_or_source_snippets(self) -> None:
        self.write("src/Taranom.Danger/Fake.cs",
                   'var secret = "OPENAI_API_KEY=never-expose-abcdef";\n')
        # The Finding contains only file, line and rule; never source contents.
        self.assertEqual(1, len(self.issues()))
        self.assertEqual(Finding("src/Taranom.Danger/Fake.cs", 1,
                                 "external-ai-credential-or-endpoint"), self.issues()[0])
        self.assertNotIn("never-expose", str(self.issues()))

    def test_real_cli_result_is_success_or_failure_as_expected(self) -> None:
        self.assertEqual(0, main(["--root", str(self.root)]))
        self.write("apps/center-web/src/app.tsx", 'import "openai";')
        self.assertEqual(1, main(["--root", str(self.root)]))

    def test_issue_order_is_stable_for_deterministic_ci(self) -> None:
        self.write("src/z.cs", 'var a="api.openai.com";')
        self.write("src/a.cs", 'var a="OPENAI_API_KEY";')
        issues = self.issues()
        self.assertEqual(issues, sorted(issues))
        self.assertEqual(issues, self.issues())


if __name__ == "__main__":
    unittest.main()
