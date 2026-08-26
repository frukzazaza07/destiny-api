"""Training, persistence, and inference for the seed CPU classifier."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import re
import unicodedata

import joblib
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import FeatureUnion, Pipeline

from .seed_data import iter_training_examples
from .taxonomy import INTENT_TO_DOMAIN, MODEL_VERSION, PERSONAL_CUSTOM

RANDOM_STATE = 1729
MIN_SERVICE_CONFIDENCE = 0.45


def normalize_question(question: str) -> str:
    normalized = unicodedata.normalize("NFKC", question).casefold().strip()
    return re.sub(r"\s+", " ", normalized)


def build_pipeline() -> Pipeline:
    features = FeatureUnion(
        [
            (
                "word",
                TfidfVectorizer(
                    preprocessor=normalize_question,
                    ngram_range=(1, 2),
                    sublinear_tf=True,
                    strip_accents=None,
                ),
            ),
            (
                "char",
                TfidfVectorizer(
                    preprocessor=normalize_question,
                    analyzer="char_wb",
                    ngram_range=(3, 5),
                    sublinear_tf=True,
                    strip_accents=None,
                ),
            ),
        ]
    )
    classifier = LogisticRegression(
        # Seed labels are clean and the 29-way softmax otherwise under-confident on
        # short bilingual questions. This value is fixed by held-out acceptance cases.
        C=3_000.0,
        class_weight="balanced",
        max_iter=4_000,
        random_state=RANDOM_STATE,
        solver="lbfgs",
    )
    return Pipeline([("features", features), ("classifier", classifier)])


@dataclass(frozen=True)
class Prediction:
    domain: str
    intent: str
    confidence: float


@dataclass
class ClassifierModel:
    pipeline: Pipeline
    model_version: str = MODEL_VERSION

    @classmethod
    def train(cls) -> "ClassifierModel":
        examples = list(iter_training_examples())
        questions = [example.question for example in examples]
        labels = [example.intent for example in examples]
        pipeline = build_pipeline()
        pipeline.fit(questions, labels)
        return cls(pipeline=pipeline)

    def predict(self, question: str) -> Prediction:
        probabilities = self.pipeline.predict_proba([question])[0]
        classifier = self.pipeline.named_steps["classifier"]
        best_index = int(probabilities.argmax())
        intent = str(classifier.classes_[best_index])
        confidence = float(probabilities[best_index])

        if confidence < MIN_SERVICE_CONFIDENCE:
            return Prediction("GENERAL", PERSONAL_CUSTOM, confidence)

        return Prediction(INTENT_TO_DOMAIN[intent], intent, confidence)

    def save(self, artifact_path: Path) -> None:
        artifact_path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "model_version": self.model_version,
            "intent_to_domain": INTENT_TO_DOMAIN,
            "pipeline": self.pipeline,
        }
        joblib.dump(payload, artifact_path)

    @classmethod
    def load(cls, artifact_path: Path) -> "ClassifierModel":
        payload = joblib.load(artifact_path)
        if payload.get("model_version") != MODEL_VERSION:
            raise ValueError("Classifier artifact model version is not supported.")
        if payload.get("intent_to_domain") != INTENT_TO_DOMAIN:
            raise ValueError("Classifier artifact taxonomy does not match this service.")
        return cls(
            pipeline=payload["pipeline"],
            model_version=payload["model_version"],
        )
