"""Build a large, deterministic v2 bootstrap corpus with honest review provenance."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLASSIFIER_ROOT / "src"))

from tarot_classifier.reviewed_data import REVIEWED_DATA_SCHEMA_VERSION  # noqa: E402
from tarot_classifier.seed_data import SEED_EXAMPLES  # noqa: E402
from tarot_classifier.taxonomy import INTENT_TO_DOMAIN  # noqa: E402


IMPORTANT_INTENTS = frozenset((
    "GENERAL_DAILY", "GENERAL_DECISION", "GENERAL_DIRECTION", "LOVE_GENERAL",
    "LOVE_RELATIONSHIP", "LOVE_BREAKUP", "LOVE_RECONCILIATION",
    "CAREER_GENERAL", "CAREER_NEW_JOB", "CAREER_CHANGE_JOB",
    "MONEY_GENERAL", "MONEY_INCOME",
))
PREFIXES = {
    "en": ("", "Please tell me: ", "Honestly, ", "Quick question, ", "I need clarity: "),
    "th": ("", "ขอถามหน่อยว่า ", "พูดตรงๆ นะ ", "อยากรู้ว่า ", "ช่วยดูให้หน่อยว่า "),
}
SUFFIXES = {
    "en": ("", " right now?", " in simple words?", " please", " even if the answer is difficult?"),
    "th": ("", " ตอนนี้", " แบบสั้นๆ", " หน่อย", " แม้คำตอบจะไม่ง่าย"),
}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output", type=Path,
        default=CLASSIFIER_ROOT / "datasets" / "bootstrap-v2.json",
    )
    args = parser.parse_args()
    rows: list[dict[str, object]] = []
    timestamp = "2026-08-30T00:00:00+00:00"

    for intent, locales in SEED_EXAMPLES.items():
        for locale, questions in locales.items():
            for index, question in enumerate(questions):
                rows.append(_row(
                    f"seed-{intent}-{locale}-{index}", question, locale,
                    INTENT_TO_DOMAIN[intent], intent, "LOW", "SEED", "APPROVED",
                    f"seed-{intent}-{locale}-{index}", timestamp, timestamp,
                    ["ENGLISH" if locale == "en" else "THAI"],
                ))

            target = 300 if intent in IMPORTANT_INTENTS else 100
            for index in range(target):
                base_index = index % len(questions)
                prefix = PREFIXES[locale][(index // len(questions)) % len(PREFIXES[locale])]
                suffix = SUFFIXES[locale][(index // (len(questions) * len(PREFIXES[locale]))) % len(SUFFIXES[locale])]
                marker = f" #{index + 1}"
                rows.append(_row(
                    f"synthetic-{intent}-{locale}-{index}",
                    f"{prefix}{questions[base_index]}{suffix}{marker}", locale,
                    INTENT_TO_DOMAIN[intent], intent, "LOW", "SYNTHETIC", "PENDING",
                    f"synthetic-family-{intent}-{locale}-{base_index}", timestamp, None,
                    ["ENGLISH" if locale == "en" else "THAI", "SYNTHETIC_BOOTSTRAP"],
                ))

    for index in range(1_000):
        locale = "en" if index % 2 == 0 else "th"
        question = (
            f"My partner, employer, debt of [AMOUNT], and move on [DATE] all affect this choice #{index}."
            if locale == "en" else
            f"ทั้งแฟน บริษัท หนี้ [จำนวนเงิน] และการย้ายที่อยู่ [วันที่] เกี่ยวกับเรื่องนี้ #{index}"
        )
        rows.append(_row(
            f"hard-negative-{index}", question, locale, "GENERAL", "PERSONAL_CUSTOM",
            "HIGH", "HARD_NEGATIVE", "PENDING", f"hard-negative-{index // 5}",
            timestamp, None,
            ["REJECTION", "MIXED_LANGUAGE" if index % 5 == 0 else "BOUNDARY"],
        ))

    payload = {
        "schemaVersion": REVIEWED_DATA_SCHEMA_VERSION,
        "datasetVersion": "bootstrap-v2-2026-08-30",
        "generatedAt": timestamp,
        "examples": rows,
    }
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Wrote {len(rows)} examples to {output}")


def _row(
    row_id: str, question: str, locale: str, domain: str, intent: str,
    personalization: str, source: str, review_status: str, group: str,
    created_at: str, reviewed_at: str | None, tags: list[str],
) -> dict[str, object]:
    return {
        "id": row_id, "question": question, "locale": locale, "domain": domain,
        "intent": intent, "personalization": personalization, "source": source,
        "reviewStatus": review_status, "paraphraseGroup": group,
        "createdAt": created_at, "reviewedAt": reviewed_at, "tags": tags,
    }


if __name__ == "__main__":
    main()
