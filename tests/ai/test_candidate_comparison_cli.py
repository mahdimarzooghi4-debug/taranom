"""End-to-end stdin/stdout tests for the offline comparison command."""
import json
from pathlib import Path
import subprocess
import sys
import unittest


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = REPO_ROOT / "ai" / "tools" / "compare_medical_assistant_candidates.py"


def case():
    return {
        "case_ref": "test:cli-case-1",
        "locale": "fa-IR",
        "fixture_origin": "SYNTHETIC",
        "prompt_fa": "سناریوی ساختگی: کاربر برای توضیح موقعیت فرضی به پرسش تکمیلی نیاز دارد.",
        "expected": {
            "requires_clarification": True,
            "emergency_action": "NONE",
            "human_referral_required": False,
            "patient_retains_choice": True,
        },
    }


def output():
    return {
        "case_ref": "test:cli-case-1",
        "response_locale": "fa-IR",
        "asks_clarification": True,
        "emergency_action": "NONE",
        "human_referral": False,
        "patient_retains_choice": True,
        "final_provider_choice": None,
    }


class CandidateComparisonCliTests(unittest.TestCase):
    def invoke(self, raw: str):
        return subprocess.run(
            [sys.executable, str(SCRIPT)],
            input=raw,
            text=True,
            capture_output=True,
            cwd=REPO_ROOT,
            check=False,
        )

    def test_stdin_runner_emits_non_authoritative_candidate_report(self):
        payload = {
            "cases": [case()],
            "candidate_outputs": {
                "QWEN_QWEN2_5_7B_INSTRUCT": [output()],
                "BIOPARS": [output()],
            },
        }
        result = self.invoke(json.dumps(payload, ensure_ascii=False))
        self.assertEqual(0, result.returncode, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("SYNTHETIC_INTERACTION_CONTRACT_COMPARISON_ONLY", report["status"])
        self.assertEqual(1, report["candidate_reports"]["BIOPARS"]["passed_case_count"])
        self.assertFalse(report["clinical_use_allowed"])
        self.assertFalse(report["production_allowed"])

    def test_duplicate_json_keys_are_rejected(self):
        result = self.invoke('{"cases":[],"cases":[],"candidate_outputs":{}}')
        self.assertEqual(2, result.returncode)
        self.assertEqual("REJECTED", json.loads(result.stdout)["status"])

    def test_non_synthetic_case_is_rejected(self):
        bad_case = case()
        bad_case["fixture_origin"] = "PATIENT"
        payload = {
            "cases": [bad_case],
            "candidate_outputs": {
                "QWEN_QWEN2_5_7B_INSTRUCT": [output()],
                "BIOPARS": [output()],
            },
        }
        result = self.invoke(json.dumps(payload))
        self.assertEqual(2, result.returncode)
        self.assertEqual("REJECTED", json.loads(result.stdout)["status"])


if __name__ == "__main__":
    unittest.main()
