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
from tarot_classifier.reviewed_data import (  # noqa: E402
    REVIEWED_DATA_SCHEMA_VERSION,
    ReviewedExample,
    load_reviewed_examples,
)
from tarot_classifier.semantic_index import (  # noqa: E402
    EMBEDDING_MODEL_VERSION,
    SemanticIntentIndex,
)
from tarot_classifier.taxonomy import REVIEWED_MODEL_PREFIX  # noqa: E402


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
                    "examples": examples,
                }
            ),
            encoding="utf-8",
        )
        return path

    def test_loads_response_envelope_and_deduplicates_identical_labels(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "reviewed.json"
            path.write_text(
                json.dumps(
                    {
                        "data": {
                            "schemaVersion": REVIEWED_DATA_SCHEMA_VERSION,
                            "examples": [
                                {
                                    "id": "first",
                                    "question": "  Should I change jobs?  ",
                                    "locale": "EN",
                                    "domain": "CAREER",
                                    "intent": "CAREER_CHANGE_JOB",
                                },
                                {
                                    "id": "duplicate",
                                    "question": "Should I change jobs?",
                                    "locale": "en",
                                    "domain": "CAREER",
                                    "intent": "CAREER_CHANGE_JOB",
                                },
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

    def test_rejects_domain_mismatch_personal_custom_and_conflicting_labels(self) -> None:
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
                    "intent": "PERSONAL_CUSTOM",
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


if __name__ == "__main__":
    unittest.main()
