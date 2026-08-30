"""Versioned classifier dataset validation and leakage-safe grouped splits."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import hashlib
import json
from pathlib import Path
import re
import unicodedata

from .taxonomy import INTENT_TO_DOMAIN, PERSONAL_CUSTOM


REVIEWED_DATA_SCHEMA_VERSION = "tarot-classifier-dataset-v2"
SUPPORTED_LOCALES = frozenset(("en", "th"))
PERSONALIZATION_LEVELS = frozenset(("LOW", "MEDIUM", "HIGH"))
SOURCES = frozenset((
    "SEED", "SYNTHETIC", "PRODUCTION_REVIEWED", "MANUAL_REVIEWED", "HARD_NEGATIVE"
))
REVIEW_STATUSES = frozenset(("PENDING", "APPROVED", "REJECTED"))
SPLITS = frozenset(("TRAIN", "VALIDATION", "TEST"))


@dataclass(frozen=True)
class ReviewedExample:
    question: str
    intent: str
    locale: str
    example_id: str | None = None
    domain: str | None = None
    personalization: str = "LOW"
    source: str = "MANUAL_REVIEWED"
    review_status: str = "APPROVED"
    paraphrase_group: str | None = None
    created_at: str | None = None
    reviewed_at: str | None = None
    split: str | None = None
    tags: tuple[str, ...] = ()
    predicted_domain: str | None = None
    predicted_intent: str | None = None
    predicted_personalization: str | None = None
    predicted_model_version: str | None = None
    reviewer_time_seconds: int | None = None

    @property
    def effective_domain(self) -> str:
        return "GENERAL" if self.intent == PERSONAL_CUSTOM else INTENT_TO_DOMAIN[self.intent]

    @property
    def group(self) -> str:
        return self.paraphrase_group or self.example_id or normalize_reviewed_question(self.question).casefold()


def normalize_reviewed_question(question: str) -> str:
    normalized = unicodedata.normalize("NFKC", question).strip()
    return re.sub(r"\s+", " ", normalized)


def load_reviewed_examples(path: Path) -> list[ReviewedExample]:
    """Load the v2 audit contract, validating all rows and returning approved truth."""

    with path.open("r", encoding="utf-8") as reviewed_file:
        payload = json.load(reviewed_file)
    if isinstance(payload, dict) and "data" in payload:
        payload = payload["data"]
    if not isinstance(payload, dict):
        raise ValueError("Reviewed dataset must be a JSON object.")
    if payload.get("schemaVersion") != REVIEWED_DATA_SCHEMA_VERSION:
        raise ValueError(f"Reviewed dataset schema must be {REVIEWED_DATA_SCHEMA_VERSION}.")
    raw_examples = payload.get("examples")
    if not isinstance(raw_examples, list):
        raise ValueError("Reviewed dataset examples must be an array.")

    examples_by_key: dict[tuple[str, str], ReviewedExample] = {}
    groups_to_split: dict[str, str] = {}
    for index, raw in enumerate(raw_examples):
        if not isinstance(raw, dict):
            raise ValueError(f"Reviewed example {index} must be an object.")
        required = (
            "question", "locale", "domain", "intent", "personalization", "source",
            "reviewStatus", "paraphraseGroup", "createdAt", "reviewedAt",
        )
        missing = [field for field in required if field not in raw]
        if missing:
            raise ValueError(f"Reviewed example {index} is missing {', '.join(missing)}.")

        question = normalize_reviewed_question(str(raw.get("question", "")))
        locale = str(raw.get("locale", "")).strip().casefold()
        domain = str(raw.get("domain", "")).strip().upper()
        intent = str(raw.get("intent", "")).strip().upper()
        personalization = str(raw.get("personalization", "")).strip().upper()
        source = str(raw.get("source", "")).strip().upper()
        review_status = str(raw.get("reviewStatus", "")).strip().upper()
        group = str(raw.get("paraphraseGroup", "")).strip()
        split_value = raw.get("split")
        split = str(split_value).strip().upper() if split_value else None
        created_at = _timestamp(raw.get("createdAt"), index, "createdAt")
        reviewed_at = _timestamp(raw.get("reviewedAt"), index, "reviewedAt", allow_none=True)

        if not question or len(question) > 500:
            raise ValueError(f"Reviewed example {index} has an invalid question.")
        if locale not in SUPPORTED_LOCALES:
            raise ValueError(f"Reviewed example {index} has unsupported locale {locale!r}.")
        if intent != PERSONAL_CUSTOM and intent not in INTENT_TO_DOMAIN:
            raise ValueError(f"Reviewed example {index} has unknown intent {intent!r}.")
        expected_domain = "GENERAL" if intent == PERSONAL_CUSTOM else INTENT_TO_DOMAIN[intent]
        if domain != expected_domain:
            raise ValueError(f"Reviewed example {index} maps intent {intent!r} to invalid domain {domain!r}.")
        if personalization not in PERSONALIZATION_LEVELS:
            raise ValueError(f"Reviewed example {index} has invalid personalization.")
        if source not in SOURCES or review_status not in REVIEW_STATUSES:
            raise ValueError(f"Reviewed example {index} has invalid source or review status.")
        if not group:
            raise ValueError(f"Reviewed example {index} has no paraphrase group.")
        if split is not None and split not in SPLITS:
            raise ValueError(f"Reviewed example {index} has invalid split.")
        if review_status == "APPROVED" and reviewed_at is None:
            raise ValueError(f"Approved example {index} must have reviewedAt.")
        if split is not None:
            existing_split = groups_to_split.setdefault(group, split)
            if existing_split != split:
                raise ValueError(f"Paraphrase group {group!r} crosses dataset splits.")

        example = ReviewedExample(
            question=question, intent=intent, locale=locale,
            example_id=str(raw["id"]) if raw.get("id") is not None else None,
            domain=domain, personalization=personalization, source=source,
            review_status=review_status, paraphrase_group=group,
            created_at=created_at, reviewed_at=reviewed_at, split=split,
            tags=tuple(sorted(str(tag).strip().upper() for tag in raw.get("tags", []) if str(tag).strip())),
            predicted_domain=_optional_upper(raw.get("predictedDomain")),
            predicted_intent=_optional_upper(raw.get("predictedIntent")),
            predicted_personalization=_optional_upper(raw.get("predictedPersonalization")),
            predicted_model_version=_optional_text(raw.get("predictedModelVersion")),
            reviewer_time_seconds=_optional_nonnegative_int(raw.get("reviewerTimeSeconds"), index),
        )
        key = (locale, question.casefold())
        existing = examples_by_key.get(key)
        if existing is not None and (existing.intent != intent or existing.personalization != personalization):
            raise ValueError(f"Reviewed dataset has conflicting labels for normalized example {index}.")
        examples_by_key[key] = existing or example

    return sorted(
        (example for example in examples_by_key.values() if example.review_status == "APPROVED"),
        key=lambda item: (item.locale, item.intent, item.question.casefold(), item.example_id or ""),
    )


def assign_grouped_splits(examples: list[ReviewedExample]) -> dict[str, list[ReviewedExample]]:
    """Assign deterministic 70/15/15 splits without separating paraphrase groups."""

    result = {"TRAIN": [], "VALIDATION": [], "TEST": []}
    group_assignments: dict[str, str] = {}
    for example in examples:
        assigned = example.split
        if assigned is None:
            bucket = int(hashlib.sha256(example.group.encode("utf-8")).hexdigest()[:8], 16) % 100
            assigned = "TRAIN" if bucket < 70 else "VALIDATION" if bucket < 85 else "TEST"
        existing = group_assignments.setdefault(example.group, assigned)
        if existing != assigned:
            raise ValueError(f"Paraphrase group {example.group!r} crosses dataset splits.")
        result[assigned].append(example)
    return result


def _timestamp(value: object, index: int, field: str, allow_none: bool = False) -> str | None:
    if value is None and allow_none:
        return None
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"Reviewed example {index} has invalid {field}.")
    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise ValueError(f"Reviewed example {index} has invalid {field}.") from error
    return value


def _optional_upper(value: object) -> str | None:
    return str(value).strip().upper() if value is not None and str(value).strip() else None


def _optional_text(value: object) -> str | None:
    return str(value).strip() if value is not None and str(value).strip() else None


def _optional_nonnegative_int(value: object, index: int) -> int | None:
    if value is None:
        return None
    if not isinstance(value, int) or value < 0:
        raise ValueError(f"Reviewed example {index} has invalid reviewerTimeSeconds.")
    return value
