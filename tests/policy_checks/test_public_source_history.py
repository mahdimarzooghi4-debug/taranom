"""TRN-TECH-063: public Git source/history provenance guard negative cases."""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.policy_checks.check_public_source_history import (  # noqa: E402
    inspect_blob, is_sensitive_path, scan, main,
)


class PublicSourceHistoryGuardTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.git('init', '-q')
        self.git('config', 'user.email', 'test@example.invalid')
        self.git('config', 'user.name', 'Synthetic Test')
        self.commit('README.md', 'Safe synthetic fixtures: test:member-a, only_synthetic_ci.\n')

    def git(self, *args: str) -> str:
        return subprocess.check_output(['git', '-C', str(self.root), *args],
                                       stderr=subprocess.PIPE).decode().strip()

    def commit(self, path: str, text: str) -> None:
        p = self.root / path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding='utf-8')
        self.git('add', path)
        self.git('commit', '-qm', 'Synthetic no-secret test commit')

    def finding_rules(self, result: dict) -> list[str]:
        return [e['rule'] for e in result['findings']]

    def test_synthetic_only_repo_has_limited_pass_without_release_approval(self) -> None:
        result = scan(self.root)
        self.assertEqual('PASS_LIMITED_SCOPE', result['status'])
        self.assertFalse(result['national_release_allowed'])
        self.assertFalse(result['independent_credential_clearance'])
        self.assertGreater(result['text_blobs_checked'], 0)

    def test_detects_access_token_in_committed_history_after_deletion(self) -> None:
        credential = 'ghp_' + 'Z' * 36
        self.commit('notes.txt', credential + '\n')
        self.git('rm', 'notes.txt')
        self.git('commit', '-qm', 'Remove credential from current HEAD')
        result = scan(self.root)
        self.assertIn('GITHUB_ACCESS_TOKEN', self.finding_rules(result))
        self.assertNotIn(credential, json.dumps(result))

    def test_detects_new_style_github_pat(self) -> None:
        value = 'github_pat_' + 'R' * 75
        self.commit('history.txt', value)
        self.assertIn('GITHUB_ACCESS_TOKEN', self.finding_rules(scan(self.root)))

    def test_aws_access_key_id_is_blocked(self) -> None:
        self.commit('aws.log', 'AKIA' + 'A' * 16)
        self.assertIn('AWS_ACCESS_KEY_ID', self.finding_rules(scan(self.root)))

    def test_private_key_header_is_blocked_without_printing_contents(self) -> None:
        header = '-----BEGIN ' + 'PRIVATE KEY-----'
        self.commit('file.txt', header + '\n' + ('B' * 60))
        result = scan(self.root)
        self.assertIn('PRIVATE_KEY_PEM', self.finding_rules(result))
        self.assertNotIn('B' * 60, json.dumps(result))

    def test_stripe_live_secret_blocked(self) -> None:
        self.commit('file.txt', 'sk_live_' + 'X' * 28)
        self.assertIn('STRIPE_LIVE_SECRET_KEY', self.finding_rules(scan(self.root)))

    def test_slack_token_blocked(self) -> None:
        self.commit('file.txt', 'xoxb-' + '1' * 14 + '-' + '2' * 14)
        self.assertIn('SLACK_ACCESS_TOKEN', self.finding_rules(scan(self.root)))

    def test_sensitive_dotenv_path_is_blocked_even_if_removed(self) -> None:
        self.commit('.env.production', 'PUBLIC=false\n')
        self.git('rm', '.env.production')
        self.git('commit', '-qm', 'Remove tracked env')
        result = scan(self.root)
        self.assertIn('SENSITIVE_TRACKED_PATH', self.finding_rules(result))

    def test_example_env_templates_are_allowed(self) -> None:
        self.commit('.env.example', 'PLACEHOLDER=replace-me\n')
        self.assertEqual('PASS_LIMITED_SCOPE', scan(self.root)['status'])

    def test_credential_filename_is_blocked(self) -> None:
        self.commit('infra/credentials.json', '{}\n')
        self.assertIn('SENSITIVE_TRACKED_PATH', self.finding_rules(scan(self.root)))

    def test_realistic_password_variable_detected_without_value_exposure(self) -> None:
        password = 'V' * 44
        self.commit('deploy.properties', 'PRODUCTION_PASSWORD=' + password + '\n')
        result = scan(self.root)
        self.assertIn('LONG_LITERAL_CREDENTIAL_ASSIGNMENT', self.finding_rules(result))
        self.assertNotIn(password, json.dumps(result))

    def test_hash_and_digest_vars_are_not_treated_as_secret_values(self) -> None:
        self.commit('meta.txt', 'TOKEN_SHA256=' + '0' * 64 + '\n')
        self.assertEqual('PASS_LIMITED_SCOPE', scan(self.root)['status'])

    def test_claimed_ci_only_test_password_not_reported(self) -> None:
        self.commit('workflow.yml', 'POSTGRES_PASSWORD: only_synthetic_ci\n')
        self.assertEqual('PASS_LIMITED_SCOPE', scan(self.root)['status'])

    def test_unreachable_blob_is_outside_declared_ref_scope(self) -> None:
        self.commit('unreachable.txt', 'harmless example')
        self.git('rm', 'unreachable.txt')
        self.git('commit', '-qm', 'Removed but still reachable: expected scanning')
        self.assertEqual('PASS_LIMITED_SCOPE', scan(self.root)['status'])

    def test_historical_sensitive_name_with_identical_blob_is_detected(self) -> None:
        self.commit('safe.txt', 'one harmless repeated content')
        self.commit('.env.production', 'one harmless repeated content')
        self.git('rm', '.env.production')
        self.git('commit', '-qm', 'Remove previous sensitive name')
        result = scan(self.root)
        self.assertIn('HISTORICAL_SENSITIVE_PATH', self.finding_rules(result))

    def test_other_fetched_branch_reachable_is_scanned(self) -> None:
        self.git('checkout', '-qb', 'synthetic-other-branch')
        self.commit('secret.txt', 'gho_' + 'K' * 35)
        self.git('checkout', '-q', '-')
        self.assertIn('GITHUB_ACCESS_TOKEN', self.finding_rules(scan(self.root)))

    def test_shallow_repository_is_refused(self) -> None:
        (self.root / '.git' / 'shallow').write_text(self.git('rev-parse', 'HEAD') + '\n')
        self.assertIn('SCAN_UNVERIFIABLE', self.finding_rules(scan(self.root)))

    def test_non_git_directory_does_not_accidentally_pass(self) -> None:
        root = self.root / 'nongit'
        root.mkdir()
        self.assertEqual('BLOCKED', scan(root)['status'])

    def test_oversized_text_blob_is_not_silently_skipped(self) -> None:
        self.commit('sensitive.log', 'safe-' * 410_000)
        self.assertIn('OVERSIZED_UNSCANNED_TEXT_OBJECT',
                      self.finding_rules(scan(self.root)))

    def test_path_detection_handles_credential_extensions(self) -> None:
        for path in ['private/id_rsa', '.aws/credentials', 'key.pfx',
                     'nested/.env.local', 'client.pem', 'state.tfstate']:
            with self.subTest(path=path):
                self.assertTrue(is_sensitive_path(path))

    def test_known_synthetic_test_property_is_narrowly_permitted(self) -> None:
        value = 'only_synthetic_untrusted_postgres_fixture_password'
        self.commit('tests/SyntheticJournalTests.cs', 'Password = "' + value + '";\n')
        self.assertEqual('PASS_LIMITED_SCOPE', scan(self.root)['status'])

    def test_test_only_plain_fixture_password_is_scoped_to_test_source(self) -> None:
        value = 'only_test_untrusted_postgres_local_password'
        self.commit('tests/SyntheticDbTests.cs', 'Password = "' + value + '";\n')
        self.assertEqual('PASS_LIMITED_SCOPE', scan(self.root)['status'])

    def test_test_only_plain_fixture_password_outside_test_source_denied(self) -> None:
        value = 'only_test_untrusted_postgres_local_password'
        self.commit('src/Production.cs', 'Password = "' + value + '";\n')
        self.assertIn('LONG_LITERAL_CREDENTIAL_ASSIGNMENT', self.finding_rules(scan(self.root)))

    def test_synthetic_looking_literal_in_production_source_remains_denied(self) -> None:
        value = 'only_synthetic_untrusted_postgres_fixture_password'
        self.commit('src/Production.cs', 'Password = "' + value + '";\n')
        self.assertIn('LONG_LITERAL_CREDENTIAL_ASSIGNMENT',
                      self.finding_rules(scan(self.root)))

    def test_synthetic_looking_mixed_password_in_tests_remains_denied(self) -> None:
        value = 'synthetic_Extra123_' + 'T' * 38
        self.commit('tests/More.cs', 'Password = "' + value + '";\n')
        self.assertIn('LONG_LITERAL_CREDENTIAL_ASSIGNMENT',
                      self.finding_rules(scan(self.root)))

    def test_production_password_name_in_tests_remains_denied(self) -> None:
        value = 'only_synthetic_untrusted_postgres_fixture_password'
        self.commit('tests/Other.cs', 'PRODUCTION_PASSWORD = "' + value + '";\n')
        self.assertIn('LONG_LITERAL_CREDENTIAL_ASSIGNMENT',
                      self.finding_rules(scan(self.root)))

    def test_scanner_does_not_flag_ordinary_decision_or_sha(self) -> None:
        self.commit('business.md', '# TRN-BIZ-040\n' + 'f' * 64 + '\n')
        self.assertEqual('PASS_LIMITED_SCOPE', scan(self.root)['status'])


if __name__ == '__main__':
    unittest.main()
