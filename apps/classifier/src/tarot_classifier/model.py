"""Training, persistence, and inference for the seed CPU classifier."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
from pathlib import Path
import re
import unicodedata

import joblib
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.calibration import CalibratedClassifierCV
from sklearn.frozen import FrozenEstimator
from sklearn.pipeline import FeatureUnion, Pipeline

from .reviewed_data import (
    REVIEWED_DATA_SCHEMA_VERSION,
    ReviewedExample,
    assign_grouped_splits,
    normalize_reviewed_question,
)
from .seed_data import iter_training_examples
from .semantic_index import EMBEDDING_MODEL_VERSION, SemanticIntentIndex, SemanticMatch
from .taxonomy import (
    ARTIFACT_SCHEMA_VERSION,
    INTENT_TO_DOMAIN,
    MODEL_VERSION,
    PERSONAL_CUSTOM,
    REVIEWED_MODEL_PREFIX,
    TAXONOMY_VERSION,
)

RANDOM_STATE = 1729
MIN_SERVICE_CONFIDENCE = 0.45
MIN_SEMANTIC_SIMILARITY = 0.82
MIN_SEMANTIC_MARGIN = 0.05
MIN_SEMANTIC_AGREEMENT = 0.60
MAX_LOGISTIC_CONFIDENCE_FOR_SEMANTIC_OVERRIDE = 0.85

DECISION_TFIDF_LOGREG = "TFIDF_LOGREG"
DECISION_HYBRID_AGREEMENT = "HYBRID_AGREEMENT"
DECISION_SEMANTIC_NEIGHBOR = "SEMANTIC_NEIGHBOR"
DECISION_SEMANTIC_CONFLICT = "SEMANTIC_CONFLICT"


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
    decision_method: str = DECISION_TFIDF_LOGREG
    semantic_similarity: float | None = None
    embedding_model_version: str | None = None


@dataclass
class ClassifierModel:
    pipeline: object
    model_version: str = MODEL_VERSION
    semantic_index: SemanticIntentIndex | None = None
    reviewed_example_count: int = 0
    reviewed_dataset_hash: str | None = None
    calibrated: bool = False
    build_timestamp: str | None = None
    evaluation_metrics: dict[str, object] | None = None

    @classmethod
    def train(
        cls,
        reviewed_examples: list[ReviewedExample] | None = None,
    ) -> "ClassifierModel":
        reviewed = list(reviewed_examples or ())
        splits = assign_grouped_splits(reviewed)
        train_reviewed = splits["TRAIN"] if reviewed else []
        examples = [*iter_training_examples(), *train_reviewed]
        questions = [example.question for example in examples]
        labels = [example.intent for example in examples]
        pipeline = build_pipeline()
        pipeline.fit(questions, labels)
        validation = splits["VALIDATION"]
        calibrated = False
        validation_labels = {example.intent for example in validation}
        if len(validation) >= 20 and validation_labels == set(pipeline.classes_):
            calibrator = CalibratedClassifierCV(
                FrozenEstimator(pipeline), method="sigmoid", cv=2
            )
            calibrator.fit(
                [example.question for example in validation],
                [example.intent for example in validation],
            )
            pipeline = calibrator
            calibrated = True
        dataset_hash = reviewed_dataset_hash(reviewed) if reviewed else None
        model_version = (
            f"{REVIEWED_MODEL_PREFIX}-{dataset_hash[:12]}"
            if dataset_hash is not None
            else MODEL_VERSION
        )
        return cls(
            pipeline=pipeline,
            model_version=model_version,
            semantic_index=SemanticIntentIndex.train(train_reviewed),
            reviewed_example_count=len(reviewed),
            reviewed_dataset_hash=dataset_hash,
            calibrated=calibrated,
            build_timestamp=datetime.now(timezone.utc).isoformat(),
        )

    def predict(self, question: str, locale: str = "en") -> Prediction:
        probabilities = self.pipeline.predict_proba([question])[0]
        classes = self.pipeline.classes_
        best_index = int(probabilities.argmax())
        logistic_intent = str(classes[best_index])
        logistic_confidence = float(probabilities[best_index])
        semantic_match = (
            self.semantic_index.find(question, locale)
            if self.semantic_index is not None
            else None
        )

        if semantic_match is not None and _is_strong_semantic_match(semantic_match):
            if semantic_match.intent == logistic_intent:
                return Prediction(
                    _domain_for_prediction(logistic_intent),
                    logistic_intent,
                    max(logistic_confidence, semantic_match.similarity),
                    DECISION_HYBRID_AGREEMENT,
                    semantic_match.similarity,
                    self.semantic_index.model_version,
                )

            if logistic_confidence < MAX_LOGISTIC_CONFIDENCE_FOR_SEMANTIC_OVERRIDE:
                return Prediction(
                    _domain_for_prediction(semantic_match.intent),
                    semantic_match.intent,
                    semantic_match.similarity,
                    DECISION_SEMANTIC_NEIGHBOR,
                    semantic_match.similarity,
                    self.semantic_index.model_version,
                )

            return Prediction(
                "GENERAL",
                PERSONAL_CUSTOM,
                max(logistic_confidence, semantic_match.similarity),
                DECISION_SEMANTIC_CONFLICT,
                semantic_match.similarity,
                self.semantic_index.model_version,
            )

        semantic_similarity = (
            semantic_match.similarity if semantic_match is not None else None
        )
        embedding_version = (
            self.semantic_index.model_version
            if semantic_match is not None and self.semantic_index is not None
            else None
        )
        if logistic_confidence < MIN_SERVICE_CONFIDENCE:
            return Prediction(
                "GENERAL",
                PERSONAL_CUSTOM,
                logistic_confidence,
                DECISION_TFIDF_LOGREG,
                semantic_similarity,
                embedding_version,
            )

        return Prediction(
            _domain_for_prediction(logistic_intent),
            logistic_intent,
            logistic_confidence,
            DECISION_TFIDF_LOGREG,
            semantic_similarity,
            embedding_version,
        )

    def save(self, artifact_path: Path) -> None:
        artifact_path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "artifact_schema_version": ARTIFACT_SCHEMA_VERSION,
            "model_version": self.model_version,
            "intent_to_domain": INTENT_TO_DOMAIN,
            "pipeline": self.pipeline,
            "semantic_index": self.semantic_index,
            "reviewed_example_count": self.reviewed_example_count,
            "reviewed_dataset_hash": self.reviewed_dataset_hash,
            "taxonomy_version": TAXONOMY_VERSION,
            "training_data_version": REVIEWED_DATA_SCHEMA_VERSION,
            "calibrated": self.calibrated,
            "build_timestamp": self.build_timestamp,
            "evaluation_metrics": self.evaluation_metrics,
        }
        joblib.dump(payload, artifact_path)

    @classmethod
    def load(cls, artifact_path: Path) -> "ClassifierModel":
        payload = joblib.load(artifact_path)
        if payload.get("artifact_schema_version") != ARTIFACT_SCHEMA_VERSION:
            raise ValueError("Classifier artifact schema version is not supported.")
        model_version = payload.get("model_version")
        if model_version != MODEL_VERSION and not str(model_version).startswith(
            f"{REVIEWED_MODEL_PREFIX}-"
        ):
            raise ValueError("Classifier artifact model version is not supported.")
        if payload.get("intent_to_domain") != INTENT_TO_DOMAIN:
            raise ValueError("Classifier artifact taxonomy does not match this service.")
        if payload.get("taxonomy_version") != TAXONOMY_VERSION:
            raise ValueError("Classifier artifact taxonomy version is not supported.")
        if payload.get("training_data_version") != REVIEWED_DATA_SCHEMA_VERSION:
            raise ValueError("Classifier artifact training-data version is not supported.")
        semantic_index = payload.get("semantic_index")
        if semantic_index is not None and (
            not isinstance(semantic_index, SemanticIntentIndex)
            or semantic_index.model_version != EMBEDDING_MODEL_VERSION
        ):
            raise ValueError("Classifier semantic index version is not supported.")
        return cls(
            pipeline=payload["pipeline"],
            model_version=model_version,
            semantic_index=semantic_index,
            reviewed_example_count=int(payload.get("reviewed_example_count", 0)),
            reviewed_dataset_hash=payload.get("reviewed_dataset_hash"),
            calibrated=bool(payload.get("calibrated", False)),
            build_timestamp=payload.get("build_timestamp"),
            evaluation_metrics=payload.get("evaluation_metrics"),
        )

    def manifest(self, seed_example_count: int) -> dict[str, object]:
        return {
            "artifactSchemaVersion": ARTIFACT_SCHEMA_VERSION,
            "modelVersion": self.model_version,
            "embeddingModelVersion": (
                self.semantic_index.model_version
                if self.semantic_index is not None
                else None
            ),
            "seedExampleCount": seed_example_count,
            "reviewedExampleCount": self.reviewed_example_count,
            "totalTrainingExampleCount": seed_example_count
            + self.reviewed_example_count,
            "reviewedDatasetHash": self.reviewed_dataset_hash,
            "taxonomyVersion": TAXONOMY_VERSION,
            "trainingDataVersion": REVIEWED_DATA_SCHEMA_VERSION,
            "calibrated": self.calibrated,
            "buildTimestamp": self.build_timestamp,
            "evaluationMetrics": self.evaluation_metrics,
        }


def reviewed_dataset_hash(examples: list[ReviewedExample]) -> str:
    canonical_rows = sorted(
        "\0".join(
            (
                example.locale,
                example.effective_domain,
                example.intent,
                example.personalization,
                example.source,
                example.review_status,
                example.group,
                example.split or "AUTO",
                normalize_reviewed_question(example.question).casefold(),
            )
        )
        for example in examples
    )
    digest = hashlib.sha256()
    for row in canonical_rows:
        digest.update(row.encode("utf-8"))
        digest.update(b"\n")
    return digest.hexdigest()


def _is_strong_semantic_match(match: SemanticMatch) -> bool:
    return (
        match.similarity >= MIN_SEMANTIC_SIMILARITY
        and match.margin >= MIN_SEMANTIC_MARGIN
        and match.agreement >= MIN_SEMANTIC_AGREEMENT
    )


def _domain_for_prediction(intent: str) -> str:
    return "GENERAL" if intent == PERSONAL_CUSTOM else INTENT_TO_DOMAIN[intent]
