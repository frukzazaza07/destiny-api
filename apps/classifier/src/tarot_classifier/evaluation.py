"""Held-out classifier evaluation and strict shared-cache quality gating."""

from __future__ import annotations

from collections import Counter, defaultdict
from typing import Iterable

from .model import ClassifierModel
from .reviewed_data import ReviewedExample
from .taxonomy import PERSONAL_CUSTOM


SHARED_CACHE_THRESHOLD = 0.90
REQUIRED_SUBSETS = ("ENGLISH", "THAI", "MIXED_LANGUAGE", "TYPO", "BOUNDARY", "REJECTION")


def evaluate_model(
    model: ClassifierModel,
    examples: Iterable[ReviewedExample],
    approved_intents: Iterable[str] = (),
) -> dict[str, object]:
    rows: list[dict[str, object]] = []
    for example in examples:
        prediction = model.predict(example.question, example.locale)
        accepted = (
            prediction.confidence > SHARED_CACHE_THRESHOLD
            and prediction.intent != PERSONAL_CUSTOM
            and example.personalization != "HIGH"
        )
        rows.append({
            "example": example,
            "prediction": prediction,
            "correct": prediction.intent == example.intent,
            "accepted": accepted,
        })

    accepted_rows = [row for row in rows if row["accepted"]]
    overall_precision = _precision(accepted_rows)
    per_intent = _group_metrics(rows, lambda row: row["prediction"].intent)
    coverage_by_intent = _group_metrics(rows, lambda row: row["example"].intent)
    per_locale = _group_metrics(rows, lambda row: row["example"].locale)
    subset_metrics = {
        subset: _summary([row for row in rows if subset in row["example"].tags])
        for subset in REQUIRED_SUBSETS
    }
    confusion = Counter(
        (row["example"].intent, row["prediction"].intent)
        for row in rows if not row["correct"]
    )
    false_high_confidence = [
        {
            "id": row["example"].example_id,
            "locale": row["example"].locale,
            "expectedIntent": row["example"].intent,
            "predictedIntent": row["prediction"].intent,
            "confidence": round(row["prediction"].confidence, 6),
            "reviewRequired": True,
        }
        for row in rows if row["accepted"] and not row["correct"]
    ]
    approved = tuple(approved_intents)
    intent_gate = all(
        intent in per_intent
        and per_intent[intent]["acceptedCount"] > 0
        and per_intent[intent]["acceptedPrecision"] >= 0.95
        for intent in approved
    )
    rejection_rows = [
        row for row in rows
        if row["example"].intent == PERSONAL_CUSTOM or row["example"].personalization == "HIGH"
    ]
    rejected_safely = [row for row in rejection_rows if not row["accepted"]]
    subset_gate = all(subset_metrics[subset]["exampleCount"] > 0 for subset in REQUIRED_SUBSETS)

    return {
        "threshold": SHARED_CACHE_THRESHOLD,
        "testExampleCount": len(rows),
        "acceptedCount": len(accepted_rows),
        "acceptedPrecision": overall_precision,
        "acceptedCoverage": len(accepted_rows) / len(rows) if rows else 0.0,
        "acceptedPrecisionByIntent": per_intent,
        "acceptedCoverageByIntent": coverage_by_intent,
        "acceptedPrecisionByLocale": per_locale,
        "subsetMetrics": subset_metrics,
        "personalCustomAndHighRejectionRate": (
            len(rejected_safely) / len(rejection_rows) if rejection_rows else 0.0
        ),
        "expectedCalibrationError": _expected_calibration_error(rows),
        "confusionPairs": [
            {"expected": expected, "predicted": predicted, "count": count}
            for (expected, predicted), count in confusion.most_common()
        ],
        "falseHighConfidenceErrorAnalysis": false_high_confidence,
        "approvedIntents": list(approved),
        "qualityGatePassed": (
            bool(rows) and bool(approved) and bool(rejection_rows) and subset_gate
            and overall_precision >= 0.97 and intent_gate
        ),
    }


def _group_metrics(rows: list[dict[str, object]], key_selector) -> dict[str, dict[str, object]]:
    groups: dict[str, list[dict[str, object]]] = defaultdict(list)
    for row in rows:
        groups[str(key_selector(row))].append(row)
    return {key: _summary(group) for key, group in sorted(groups.items())}


def _summary(rows: list[dict[str, object]]) -> dict[str, object]:
    accepted = [row for row in rows if row["accepted"]]
    return {
        "exampleCount": len(rows),
        "acceptedCount": len(accepted),
        "acceptedPrecision": _precision(accepted),
        "acceptedCoverage": len(accepted) / len(rows) if rows else 0.0,
    }


def _precision(rows: list[dict[str, object]]) -> float:
    return sum(bool(row["correct"]) for row in rows) / len(rows) if rows else 0.0


def _expected_calibration_error(rows: list[dict[str, object]], bins: int = 10) -> float:
    if not rows:
        return 0.0
    error = 0.0
    for bucket in range(bins):
        lower = bucket / bins
        upper = (bucket + 1) / bins
        selected = [
            row for row in rows
            if lower <= row["prediction"].confidence <= upper
            and (bucket == bins - 1 or row["prediction"].confidence < upper)
        ]
        if not selected:
            continue
        accuracy = sum(bool(row["correct"]) for row in selected) / len(selected)
        confidence = sum(row["prediction"].confidence for row in selected) / len(selected)
        error += (len(selected) / len(rows)) * abs(accuracy - confidence)
    return error
