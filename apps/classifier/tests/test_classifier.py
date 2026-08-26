from __future__ import annotations

from pathlib import Path
import sys
import tempfile
import unittest


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLASSIFIER_ROOT / "src"))

from tarot_classifier.model import ClassifierModel  # noqa: E402
from tarot_classifier.personalization import detect_personalization  # noqa: E402
from tarot_classifier.seed_data import SEED_EXAMPLES, validate_seed_corpus  # noqa: E402
from tarot_classifier.taxonomy import (  # noqa: E402
    ALL_INTENTS,
    INTENT_TO_DOMAIN,
    MODEL_VERSION,
)


EXPECTED_INTENTS = {
    "GENERAL_DAILY",
    "GENERAL_DECISION",
    "GENERAL_DIRECTION",
    "LOVE_GENERAL",
    "LOVE_SINGLE",
    "LOVE_RELATIONSHIP",
    "LOVE_BREAKUP",
    "LOVE_RECONCILIATION",
    "LOVE_NEW_PERSON",
    "LOVE_COMMITMENT",
    "LOVE_DECISION",
    "CAREER_GENERAL",
    "CAREER_NEW_JOB",
    "CAREER_CHANGE_JOB",
    "CAREER_PROMOTION",
    "CAREER_BUSINESS",
    "CAREER_DECISION",
    "CAREER_CONFLICT",
    "MONEY_GENERAL",
    "MONEY_INCOME",
    "MONEY_INVESTMENT",
    "MONEY_BUSINESS",
    "MONEY_DEBT",
    "MONEY_PURCHASE",
    "MONEY_DECISION",
    "FAMILY_GENERAL",
    "FAMILY_CONFLICT",
    "PERSONAL_GROWTH_GENERAL",
    "PERSONAL_GROWTH_HEALING",
    "PERSONAL_CUSTOM",
}


class TaxonomyTests(unittest.TestCase):
    def test_taxonomy_matches_csharp_contract(self) -> None:
        self.assertEqual(EXPECTED_INTENTS, set(ALL_INTENTS))
        self.assertEqual(EXPECTED_INTENTS - {"PERSONAL_CUSTOM"}, set(INTENT_TO_DOMAIN))

    def test_every_learned_intent_has_bilingual_seed_examples(self) -> None:
        validate_seed_corpus()
        for intent in INTENT_TO_DOMAIN:
            with self.subTest(intent=intent):
                self.assertGreaterEqual(len(SEED_EXAMPLES[intent]["en"]), 4)
                self.assertGreaterEqual(len(SEED_EXAMPLES[intent]["th"]), 4)


class ModelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.model = ClassifierModel.train()

    def assert_prediction(
        self,
        question: str,
        expected_domain: str,
        expected_intent: str,
        minimum_confidence: float = 0.0,
    ) -> None:
        prediction = self.model.predict(question)
        self.assertEqual(expected_domain, prediction.domain)
        self.assertEqual(expected_intent, prediction.intent)
        self.assertGreaterEqual(prediction.confidence, minimum_confidence)
        self.assertGreaterEqual(prediction.confidence, 0.0)
        self.assertLessEqual(prediction.confidence, 1.0)

    def test_common_english_job_change_clears_cache_threshold(self) -> None:
        self.assert_prediction(
            "Should I change my job?",
            "CAREER",
            "CAREER_CHANGE_JOB",
            0.85,
        )

    def test_common_thai_job_change_clears_cache_threshold(self) -> None:
        self.assert_prediction(
            "ฉันควรเปลี่ยนงานไหม",
            "CAREER",
            "CAREER_CHANGE_JOB",
            0.85,
        )

    def test_predicts_additional_english_and_thai_intents(self) -> None:
        cases = (
            ("Can I reconcile with my former lover?", "LOVE", "LOVE_RECONCILIATION"),
            ("Should I put money into this investment?", "MONEY", "MONEY_INVESTMENT"),
            ("ฉันจะปลดหนี้ทั้งหมดได้อย่างไร", "MONEY", "MONEY_DEBT"),
            ("ฉันควรรับมือการทะเลาะในบ้านอย่างไร", "FAMILY", "FAMILY_CONFLICT"),
        )
        for question, domain, intent in cases:
            with self.subTest(question=question):
                self.assert_prediction(question, domain, intent)

    def test_confidence_is_raw_probability_bound(self) -> None:
        prediction = self.model.predict("What lies ahead?")
        self.assertGreaterEqual(prediction.confidence, 0.0)
        self.assertLessEqual(prediction.confidence, 1.0)

    def test_artifact_save_and_load_preserves_predictions(self) -> None:
        expected = self.model.predict("Should I seek a different employer?")
        with tempfile.TemporaryDirectory() as directory:
            artifact = Path(directory) / "classifier.joblib"
            self.model.save(artifact)
            loaded = ClassifierModel.load(artifact)
            actual = loaded.predict("Should I seek a different employer?")

        self.assertEqual(MODEL_VERSION, loaded.model_version)
        self.assertEqual(expected, actual)


class PersonalizationTests(unittest.TestCase):
    def test_short_generic_question_is_low(self) -> None:
        self.assertEqual("LOW", detect_personalization("Should I change my job?"))

    def test_detailed_question_is_high(self) -> None:
        question = (
            "I have worked with my brother for 8 years and we are considering "
            "selling our company because he plans to move overseas. What should I do?"
        )
        self.assertEqual("HIGH", detect_personalization(question))

    def test_named_context_is_high(self) -> None:
        self.assertEqual(
            "HIGH",
            detect_personalization("Should I accept the offer from my manager Daniel?"),
        )

    def test_thai_detailed_context_is_high(self) -> None:
        self.assertEqual(
            "HIGH",
            detect_personalization("ฉันทำงานกับพี่ชายมา 6 ปี เพราะตอนนี้เขาจะย้ายประเทศ ฉันควรออกจากบริษัทไหม"),
        )


if __name__ == "__main__":
    unittest.main()
