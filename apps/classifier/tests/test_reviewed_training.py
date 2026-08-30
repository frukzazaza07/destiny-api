from __future__ import annotations

import json
from pathlib import Path
import sys
import tempfile
import unittest

import joblib


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLASSIFIER_ROOT / "src"))

from tarot_classifier.model import ClassifierModel  # noqa: E402
from tarot_classifier.evaluation import REQUIRED_SUBSETS, evaluate_model  # noqa: E402
from tarot_classifier.reviewed_data import (  # noqa: E402
    REVIEWED_DATA_SCHEMA_VERSION,
    ReviewedExample,
    assign_grouped_splits,
    load_reviewed_examples,
)
from tarot_classifier.semantic_index import (  # noqa: E402
    EMBEDDING_MODEL_VERSION,
    SemanticIntentIndex,
)
from tarot_classifier.taxonomy import INTENT_TO_DOMAIN, REVIEWED_MODEL_PREFIX  # noqa: E402


def reviewed_examples() -> list[ReviewedExample]:
    return [
        ReviewedExample("Should I leave my current job now?", "CAREER_CHANGE_JOB", "en", "1"),
        ReviewedExample("Is it time to change my current job?", "CAREER_CHANGE_JOB", "en", "2"),
        ReviewedExample("Should I quit this company for a new role?", "CAREER_CHANGE_JOB", "en", "3"),
        ReviewedExample("Can my former partner and I reconcile?", "LOVE_RECONCILIATION", "en", "4"),
        ReviewedExample("Will my ex and I get back together?", "LOVE_RECONCILIATION", "en", "5"),
        ReviewedExample("Is reconciliation possible with an old lover?", "LOVE_RECONCILIATION", "en", "6"),
    ]


class ReviewedDataTests(unittest.TestCase):
    def _write_dataset(self, directory: str, examples: list[dict]) -> Path:
        path = Path(directory) / "reviewed.json"
        path.write_text(
            json.dumps(
                {
                    "schemaVersion": REVIEWED_DATA_SCHEMA_VERSION,
                    "examples": [self._complete(example, index) for index, example in enumerate(examples)],
                }
            ),
            encoding="utf-8",
        )
        return path

    @staticmethod
    def _complete(example: dict, index: int) -> dict:
        return {
            "personalization": "LOW",
            "source": "MANUAL_REVIEWED",
            "reviewStatus": "APPROVED",
            "paraphraseGroup": f"group-{index}",
            "createdAt": "2026-08-30T00:00:00+00:00",
            "reviewedAt": "2026-08-30T00:01:00+00:00",
            **example,
        }

    def test_loads_response_envelope_and_deduplicates_identical_labels(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "reviewed.json"
            path.write_text(
                json.dumps(
                    {
                        "data": {
                            "schemaVersion": REVIEWED_DATA_SCHEMA_VERSION,
                            "examples": [self._complete({
                                    "id": "first",
                                    "question": "  Should I change jobs?  ",
                                    "locale": "EN",
                                    "domain": "CAREER",
                                    "intent": "CAREER_CHANGE_JOB",
                                }, 0), self._complete({
                                    "id": "duplicate",
                                    "question": "Should I change jobs?",
                                    "locale": "en",
                                    "domain": "CAREER",
                                    "intent": "CAREER_CHANGE_JOB",
                                }, 1),
                            ],
                        }
                    }
                ),
                encoding="utf-8",
            )

            examples = load_reviewed_examples(path)

        self.assertEqual(1, len(examples))
        self.assertEqual("Should I change jobs?", examples[0].question)
        self.assertEqual("en", examples[0].locale)

    def test_rejects_domain_mismatch_unknown_intent_and_conflicting_labels(self) -> None:
        invalid_sets = (
            [
                {
                    "question": "Should I leave?",
                    "locale": "en",
                    "domain": "LOVE",
                    "intent": "CAREER_CHANGE_JOB",
                }
            ],
            [
                {
                    "question": "Private question",
                    "locale": "en",
                    "domain": "GENERAL",
                    "intent": "NOT_IN_TAXONOMY",
                }
            ],
            [
                {
                    "question": "Same question",
                    "locale": "en",
                    "domain": "CAREER",
                    "intent": "CAREER_CHANGE_JOB",
                },
                {
                    "question": "same question",
                    "locale": "en",
                    "domain": "LOVE",
                    "intent": "LOVE_GENERAL",
                },
            ],
        )

        for invalid_examples in invalid_sets:
            with self.subTest(invalid_examples=invalid_examples):
                with tempfile.TemporaryDirectory() as directory:
                    path = self._write_dataset(directory, invalid_examples)
                    with self.assertRaises(ValueError):
                        load_reviewed_examples(path)

    def test_rejects_missing_contract_fields_and_split_leakage(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            missing = Path(directory) / "missing.json"
            missing.write_text(json.dumps({
                "schemaVersion": REVIEWED_DATA_SCHEMA_VERSION,
                "examples": [{"question": "Incomplete"}],
            }), encoding="utf-8")
            with self.assertRaises(ValueError):
                load_reviewed_examples(missing)

            crossing = [
                {
                    "question": "Should I change jobs?", "locale": "en",
                    "domain": "CAREER", "intent": "CAREER_CHANGE_JOB",
                    "paraphraseGroup": "same-family", "split": "TRAIN",
                },
                {
                    "question": "Is a new role right?", "locale": "en",
                    "domain": "CAREER", "intent": "CAREER_CHANGE_JOB",
                    "paraphraseGroup": "same-family", "split": "TEST",
                },
            ]
            with self.assertRaises(ValueError):
                load_reviewed_examples(self._write_dataset(directory, crossing))

    def test_grouped_split_never_separates_a_paraphrase_family(self) -> None:
        examples = [
            ReviewedExample(f"Question {index}", "GENERAL_DIRECTION", "en",
                            str(index), paraphrase_group=f"family-{index // 3}")
            for index in range(30)
        ]
        splits = assign_grouped_splits(examples)
        assignments = {
            example.group: split
            for split, rows in splits.items()
            for example in rows
        }
        for split, rows in splits.items():
            self.assertTrue(all(assignments[row.group] == split for row in rows))

    def test_personal_custom_is_valid_reviewed_rejection_truth(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = self._write_dataset(directory, [{
                "question": "My work, debt, family and relationship all overlap.",
                "locale": "en", "domain": "GENERAL", "intent": "PERSONAL_CUSTOM",
                "personalization": "HIGH", "source": "HARD_NEGATIVE",
                "paraphraseGroup": "rejection-1", "tags": ["REJECTION"],
            }])
            examples = load_reviewed_examples(path)

        self.assertEqual("PERSONAL_CUSTOM", examples[0].intent)
        self.assertEqual("HIGH", examples[0].personalization)


class SemanticIndexTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.examples = reviewed_examples()
        cls.index = SemanticIntentIndex.train(cls.examples)

    def test_dense_index_finds_a_similar_reviewed_intent(self) -> None:
        self.assertIsNotNone(self.index)
        match = self.index.find("Should I change my current job now?", "en")

        self.assertIsNotNone(match)
        self.assertEqual("CAREER_CHANGE_JOB", match.intent)
        self.assertGreaterEqual(match.similarity, 0.82)
        self.assertGreaterEqual(match.margin, 0.05)
        self.assertGreaterEqual(match.agreement, 0.60)

    def test_index_does_not_cross_locale_boundaries(self) -> None:
        self.assertIsNotNone(self.index)
        self.assertIsNone(self.index.find("Should I change jobs?", "th"))


class ReviewedModelArtifactTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.examples = reviewed_examples()
        cls.model = ClassifierModel.train(cls.examples)

    def test_reviewed_data_creates_versioned_hybrid_model(self) -> None:
        self.assertTrue(self.model.model_version.startswith(f"{REVIEWED_MODEL_PREFIX}-"))
        self.assertEqual(len(self.examples), self.model.reviewed_example_count)
        self.assertIsNotNone(self.model.reviewed_dataset_hash)
        self.assertIsNotNone(self.model.semantic_index)

        prediction = self.model.predict("Should I change my current job now?", "en")
        self.assertEqual("CAREER", prediction.domain)
        self.assertEqual("CAREER_CHANGE_JOB", prediction.intent)
        self.assertIsNotNone(prediction.semantic_similarity)
        self.assertEqual(EMBEDDING_MODEL_VERSION, prediction.embedding_model_version)

    def test_artifact_round_trip_preserves_semantic_predictions_without_raw_rows(self) -> None:
        expected = self.model.predict("Should I change my current job now?", "en")
        with tempfile.TemporaryDirectory() as directory:
            artifact = Path(directory) / "classifier.joblib"
            self.model.save(artifact)
            payload = joblib.load(artifact)
            loaded = ClassifierModel.load(artifact)
            actual = loaded.predict("Should I change my current job now?", "en")

        self.assertEqual(expected, actual)
        self.assertNotIn("reviewed_examples", payload)
        self.assertNotIn("questions", vars(payload["semantic_index"]))

    def test_manifest_contains_counts_and_hash_but_no_questions(self) -> None:
        manifest = self.model.manifest(seed_example_count=100)
        self.assertEqual(len(self.examples), manifest["reviewedExampleCount"])
        self.assertEqual(106, manifest["totalTrainingExampleCount"])
        self.assertEqual(self.model.reviewed_dataset_hash, manifest["reviewedDatasetHash"])
        self.assertNotIn("questions", manifest)
        self.assertEqual("TAXONOMY_V1", manifest["taxonomyVersion"])
        self.assertEqual(REVIEWED_DATA_SCHEMA_VERSION, manifest["trainingDataVersion"])
        self.assertIsNotNone(manifest["buildTimestamp"])

    def test_probability_calibration_uses_explicit_validation_split(self) -> None:
        examples = []
        for intent in INTENT_TO_DOMAIN:
            for index in range(3):
                examples.append(ReviewedExample(
                    question=f"Held out calibration wording {index} for {intent}",
                    intent=intent,
                    locale="en",
                    example_id=f"{intent}-{index}",
                    paraphrase_group=f"calibration-{intent}-{index}",
                    split="VALIDATION" if index < 2 else "TRAIN",
                ))

        model = ClassifierModel.train(examples)

        self.assertTrue(model.calibrated)
        prediction = model.predict("Could changing jobs be right?", "en")
        self.assertGreaterEqual(prediction.confidence, 0.0)
        self.assertLessEqual(prediction.confidence, 1.0)

    def test_final_report_contains_locale_intent_subsets_and_calibration(self) -> None:
        tagged = [
            ReviewedExample(
                question=f"Tagged final example {index}",
                intent="GENERAL_DIRECTION",
                locale="en" if index % 2 == 0 else "th",
                example_id=str(index),
                tags=(subset,),
            )
            for index, subset in enumerate(REQUIRED_SUBSETS)
        ]
        report = evaluate_model(self.model, tagged, ["GENERAL_DIRECTION"])

        self.assertIn("acceptedPrecisionByIntent", report)
        self.assertIn("acceptedCoverageByIntent", report)
        self.assertIn("acceptedPrecisionByLocale", report)
        self.assertIn("expectedCalibrationError", report)
        self.assertEqual(set(REQUIRED_SUBSETS), set(report["subsetMetrics"]))


if __name__ == "__main__":
    unittest.main()
