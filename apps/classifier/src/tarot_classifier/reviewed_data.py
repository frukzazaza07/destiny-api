"""Load and validate consented, human-reviewed classifier examples."""

from __future__ import annotations

from dataclasses import dataclass
import json
from pathlib import Path
import re
import unicodedata

from .taxonomy import INTENT_TO_DOMAIN, PERSONAL_CUSTOM


REVIEWED_DATA_SCHEMA_VERSION = "tarot-classifier-reviewed-v1"
SUPPORTED_LOCALES = frozenset(("en", "th"))


@dataclass(frozen=True)
class ReviewedExample:
    question: str
    intent: str
    locale: str
    example_id: str | None = None


def normalize_reviewed_question(question: str) -> str:
    normalized = unicodedata.normalize("NFKC", question).strip()
    return re.sub(r"\s+", " ", normalized)


def load_reviewed_examples(path: Path) -> list[ReviewedExample]:
    """Read the stable admin export format and reject invalid or conflicting labels."""

    with path.open("r", encoding="utf-8") as reviewed_file:
        payload = json.load(reviewed_file)

    if isinstance(payload, dict) and "data" in payload:
        payload = payload["data"]
    if not isinstance(payload, dict):
        raise ValueError("Reviewed dataset must be a JSON object.")
    if payload.get("schemaVersion") != REVIEWED_DATA_SCHEMA_VERSION:
        raise ValueError(
            f"Reviewed dataset schema must be {REVIEWED_DATA_SCHEMA_VERSION}."
        )

    raw_examples = payload.get("examples")
    if not isinstance(raw_examples, list):
        raise ValueError("Reviewed dataset examples must be an array.")

    examples_by_key: dict[tuple[str, str], ReviewedExample] = {}
    for index, raw_example in enumerate(raw_examples):
        if not isinstance(raw_example, dict):
            raise ValueError(f"Reviewed example {index} must be an object.")

        question = normalize_reviewed_question(str(raw_example.get("question", "")))
        locale = str(raw_example.get("locale", "")).strip().casefold()
        domain = str(raw_example.get("domain", "")).strip().upper()
        intent = str(raw_example.get("intent", "")).strip().upper()
        example_id_value = raw_example.get("id")
        example_id = str(example_id_value) if example_id_value is not None else None

        if not question:
            raise ValueError(f"Reviewed example {index} has no question.")
        if len(question) > 500:
            raise ValueError(f"Reviewed example {index} exceeds 500 characters.")
        if locale not in SUPPORTED_LOCALES:
            raise ValueError(f"Reviewed example {index} has unsupported locale {locale!r}.")
        if intent == PERSONAL_CUSTOM or intent not in INTENT_TO_DOMAIN:
            raise ValueError(f"Reviewed example {index} has unknown learned intent {intent!r}.")
        if INTENT_TO_DOMAIN[intent] != domain:
            raise ValueError(
                f"Reviewed example {index} maps intent {intent!r} to invalid domain {domain!r}."
            )

        example = ReviewedExample(question, intent, locale, example_id)
        key = (locale, question.casefold())
        existing = examples_by_key.get(key)
        if existing is not None and existing.intent != intent:
            raise ValueError(
                f"Reviewed dataset has conflicting labels for normalized example {index}."
            )
        examples_by_key[key] = existing or example

    return sorted(
        examples_by_key.values(),
        key=lambda example: (
            example.locale,
            example.intent,
            example.question.casefold(),
            example.example_id or "",
        ),
    )
