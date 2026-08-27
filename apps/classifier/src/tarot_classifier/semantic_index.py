"""CPU semantic-intent lookup using dense latent-semantic TF-IDF embeddings."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from sklearn.decomposition import TruncatedSVD
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.pipeline import FeatureUnion
from sklearn.preprocessing import normalize

from .reviewed_data import ReviewedExample


EMBEDDING_MODEL_VERSION = "tfidf-svd-semantic-v1"
DEFAULT_NEIGHBOR_COUNT = 5


def _normalize_for_embedding(question: str) -> str:
    return " ".join(question.casefold().split())


def _build_features() -> FeatureUnion:
    return FeatureUnion(
        [
            (
                "word",
                TfidfVectorizer(
                    preprocessor=_normalize_for_embedding,
                    ngram_range=(1, 2),
                    sublinear_tf=True,
                ),
            ),
            (
                "char",
                TfidfVectorizer(
                    preprocessor=_normalize_for_embedding,
                    analyzer="char_wb",
                    ngram_range=(3, 5),
                    sublinear_tf=True,
                ),
            ),
        ]
    )


@dataclass(frozen=True)
class SemanticMatch:
    intent: str
    similarity: float
    margin: float
    agreement: float


@dataclass
class SemanticIntentIndex:
    """A persisted embedding model and reviewed-vector index without raw questions."""

    features: FeatureUnion
    reducer: TruncatedSVD
    vectors: np.ndarray
    intents: tuple[str, ...]
    locales: tuple[str, ...]
    model_version: str = EMBEDDING_MODEL_VERSION

    @classmethod
    def train(
        cls,
        examples: list[ReviewedExample],
    ) -> "SemanticIntentIndex | None":
        if len(examples) < 3:
            return None

        features = _build_features()
        sparse_vectors = features.fit_transform(
            [example.question for example in examples]
        )
        component_count = min(
            128,
            sparse_vectors.shape[0] - 1,
            sparse_vectors.shape[1] - 1,
        )
        if component_count < 2:
            return None

        reducer = TruncatedSVD(n_components=component_count, random_state=1729)
        vectors = reducer.fit_transform(sparse_vectors)
        vectors = normalize(vectors, norm="l2").astype(np.float32, copy=False)
        return cls(
            features=features,
            reducer=reducer,
            vectors=vectors,
            intents=tuple(example.intent for example in examples),
            locales=tuple(example.locale for example in examples),
        )

    def find(self, question: str, locale: str) -> SemanticMatch | None:
        locale_indices = [
            index
            for index, example_locale in enumerate(self.locales)
            if example_locale == locale
        ]
        if not locale_indices:
            return None

        sparse_query = self.features.transform([question])
        query = self.reducer.transform(sparse_query)
        query = normalize(query, norm="l2").astype(np.float32, copy=False)[0]
        if not np.any(query):
            return None

        candidate_vectors = self.vectors[locale_indices]
        similarities = candidate_vectors @ query
        ranked_local = np.argsort(similarities)[::-1]
        neighbor_count = min(DEFAULT_NEIGHBOR_COUNT, len(ranked_local))
        ranked = [locale_indices[int(index)] for index in ranked_local[:neighbor_count]]

        intent_scores: dict[str, float] = {}
        intent_counts: dict[str, int] = {}
        for global_index in ranked:
            intent = self.intents[global_index]
            similarity = max(0.0, float(self.vectors[global_index] @ query))
            intent_scores[intent] = intent_scores.get(intent, 0.0) + similarity
            intent_counts[intent] = intent_counts.get(intent, 0) + 1

        if not intent_scores:
            return None

        winning_intent = max(
            intent_scores,
            key=lambda intent: (intent_scores[intent], intent_counts[intent], intent),
        )
        winning_similarities = [
            float(self.vectors[index] @ query)
            for index in ranked
            if self.intents[index] == winning_intent
        ]
        similarity = max(winning_similarities)
        other_similarities = [
            float(self.vectors[index] @ query)
            for index in ranked
            if self.intents[index] != winning_intent
        ]
        runner_up = max(other_similarities, default=0.0)
        agreement = intent_counts[winning_intent] / neighbor_count
        return SemanticMatch(
            intent=winning_intent,
            similarity=max(0.0, min(1.0, similarity)),
            margin=max(0.0, min(1.0, similarity - runner_up)),
            agreement=agreement,
        )
