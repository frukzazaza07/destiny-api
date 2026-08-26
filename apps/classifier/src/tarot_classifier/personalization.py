"""Conservative, model-independent personalization detection."""

from __future__ import annotations

import re
import unicodedata

_NUMBER_RE = re.compile(r"\d")
_LATIN_NAMED_CONTEXT_RE = re.compile(
    r"\b(?:named|called|with|manager|boss|partner|husband|wife|friend|brother|sister)\s+"
    r"[A-Z][a-z]{2,}\b"
)
_THAI_NAMED_CONTEXT_RE = re.compile(
    r"(?:ชื่อ|คุณ|นาย|นาง|หัวหน้าชื่อ|แฟนชื่อ)[\s\u0E00-\u0E7F]{2,24}"
)
_DETAIL_MARKERS = (
    "because",
    "since",
    "after",
    "before",
    "currently",
    "for years",
    "for months",
    "my manager",
    "my company",
    "my partner",
    "my family",
    "เพราะ",
    "เนื่องจาก",
    "หลังจาก",
    "ก่อนหน้านี้",
    "ตอนนี้",
    "มาหลายปี",
    "หัวหน้าของฉัน",
    "บริษัทของฉัน",
    "แฟนของฉัน",
    "ครอบครัวของฉัน",
)


def detect_personalization(question: str) -> str:
    """Estimate how unsafe it would be to reuse a generic finished answer."""

    normalized = unicodedata.normalize("NFKC", question).strip()
    lowered = normalized.casefold()
    word_count = len(re.findall(r"\S+", normalized))
    detail_count = sum(marker in lowered for marker in _DETAIL_MARKERS)
    has_number = bool(_NUMBER_RE.search(normalized))
    has_named_context = bool(
        _LATIN_NAMED_CONTEXT_RE.search(normalized)
        or _THAI_NAMED_CONTEXT_RE.search(normalized)
    )

    if (
        len(normalized) >= 160
        or word_count >= 28
        or has_number
        or has_named_context
        or detail_count >= 3
    ):
        return "HIGH"

    if len(normalized) >= 85 or word_count >= 14 or detail_count >= 1:
        return "MEDIUM"

    return "LOW"
