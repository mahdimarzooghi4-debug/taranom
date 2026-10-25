"""Synthetic-only tests for Persian assistant interaction invariants."""
import unittest

from ai.taranom_internal.medical_assistant_eval import (
    EvaluationRejected, check_candidate_output, evaluate_batch, summarize, validate_case,
)


def case(ref="test:case-1", *, emergency="NONE", clarification=True, human=False):
    return {
        "case_ref": ref,
        "locale": "fa-IR",
        "fixture_origin": "SYNTHETIC",
        "prompt_fa": "سناریوی ساختگی: اطلاعات کافی برای راهنمایی هنوز وارد نشده است.",
        "expected": {
            "requires_clarification": clarification,
            "emergency_action": emergency,
            "human_referral_required": human,
            "patient_retains_choice": True,
        },
    }


def output(validated, *, emergency="NONE", clarification=True, human=False):
    return {
        "case_ref": validated.case_ref,
        "response_locale": "fa-IR",
        "asks_clarification": clarification,
        "emergency_action": emergency,
        "human_referral": human,
        "patient_retains_choice": True,
        "final_provider_choice": None,
    }


class MedicalAssistantEvaluationTests(unittest.TestCase):
    def test_synthetic_persian_case_and_output_are_contract_only(self):
        c = validate_case(case())
        result = check_candidate_output(c, output(c))
        self.assertTrue(result["passed"])
        self.assertEqual("NOT_EVALUATED_NO_QUALIFIED_GOLD_SET", result["diagnostic_accuracy"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["training_allowed"])
        self.assertFalse(result["production_allowed"])

    def test_emergency_fixture_requires_er_and_human_referral(self):
        c = validate_case(case(emergency="ER_NOW", human=True))
        self.assertTrue(check_candidate_output(
            c, output(c, emergency="ER_NOW", human=True))["passed"])
        missed = check_candidate_output(c, output(c))
        self.assertFalse(missed["passed"])

    def test_patient_must_keep_provider_choice(self):
        c = validate_case(case())
        bad = output(c)
        bad["final_provider_choice"] = "test:doctor-1"
        with self.assertRaisesRegex(EvaluationRejected, "PATIENT_AUTONOMY"):
            check_candidate_output(c, bad)
        bad_case = case()
        bad_case["expected"]["patient_retains_choice"] = False
        with self.assertRaisesRegex(EvaluationRejected, "PATIENT_MUST_RETAIN"):
            validate_case(bad_case)

    def test_non_synthetic_patient_input_and_unknown_fields_are_denied(self):
        for bad in (
            dict(case(), fixture_origin="PATIENT"),
            dict(case(), case_ref="member:123"),
            {**case(), "patient_id": "test:person"},
            dict(case(), prompt_fa="متن واقعی بدون برچسب سناریوی ساختگی"),
        ):
            with self.subTest(bad=bad), self.assertRaises(EvaluationRejected):
                validate_case(bad)

    def test_unhashable_action_values_are_rejected_cleanly(self):
        bad_case = case()
        bad_case["expected"]["emergency_action"] = []
        with self.assertRaisesRegex(EvaluationRejected, "UNKNOWN_EMERGENCY_EXPECTATION"):
            validate_case(bad_case)
        c = validate_case(case())
        bad_output = output(c)
        bad_output["emergency_action"] = []
        with self.assertRaisesRegex(EvaluationRejected, "UNKNOWN_EMERGENCY_OUTPUT"):
            check_candidate_output(c, bad_output)

    def test_batch_evaluator_aggregates_only_synthetic_contract_results(self):
        cases = [case("test:case-1"), case("test:case-2", clarification=False)]
        validated = [validate_case(item) for item in cases]
        outputs = [output(validated[0]), output(validated[1], clarification=False)]
        report = evaluate_batch(cases, outputs)
        self.assertEqual(2, report["summary"]["case_count"])
        self.assertEqual(2, report["summary"]["passed_case_count"])
        self.assertEqual("NOT_EVALUATED_NO_QUALIFIED_GOLD_SET", report["diagnostic_accuracy"])
        self.assertFalse(report["clinical_use_allowed"])
        self.assertFalse(report["production_allowed"])

    def test_batch_rejects_mismatch_empty_and_duplicate_refs(self):
        with self.assertRaisesRegex(EvaluationRejected, "BATCH_LENGTH_MISMATCH"):
            evaluate_batch([case()], [])
        with self.assertRaisesRegex(EvaluationRejected, "NONEMPTY_SYNTHETIC_BATCH"):
            evaluate_batch([], [])
        duplicate = [case("test:same"), case("test:same")]
        with self.assertRaisesRegex(EvaluationRejected, "DUPLICATE_SYNTHETIC_CASE_REF"):
            evaluate_batch(duplicate, [output(validate_case(duplicate[0]))] * 2)

    def test_emergency_cannot_be_defined_without_human_referral(self):
        with self.assertRaisesRegex(EvaluationRejected, "EMERGENCY_REQUIRES"):
            validate_case(case(emergency="ER_NOW", human=False))

    def test_case_lineage_and_persian_response_are_required(self):
        c = validate_case(case())
        wrong_ref = output(c)
        wrong_ref["case_ref"] = "test:other"
        with self.assertRaisesRegex(EvaluationRejected, "LINEAGE"):
            check_candidate_output(c, wrong_ref)
        wrong_locale = output(c)
        wrong_locale["response_locale"] = "en"
        with self.assertRaisesRegex(EvaluationRejected, "PERSIAN_RESPONSE"):
            check_candidate_output(c, wrong_locale)

    def test_summary_never_claims_diagnostic_accuracy_or_authority(self):
        c = validate_case(case())
        result = check_candidate_output(c, output(c))
        report = summarize([result])
        self.assertEqual(1, report["case_count"])
        self.assertEqual(1, report["passed_case_count"])
        self.assertEqual("NOT_EVALUATED_NO_QUALIFIED_GOLD_SET", report["diagnostic_accuracy"])
        self.assertFalse(report["clinical_use_allowed"])
        self.assertFalse(report["production_allowed"])
        with self.assertRaises(EvaluationRejected):
            summarize([])
        forged = {"status": "SYNTHETIC_INTERACTION_CONTRACT_ONLY", "passed": True}
        with self.assertRaisesRegex(EvaluationRejected, "UNTRUSTED_EVALUATION_RESULT"):
            summarize([forged])


if __name__ == "__main__":
    unittest.main()
