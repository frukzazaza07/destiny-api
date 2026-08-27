"""Async gRPC implementation for question classification."""

from __future__ import annotations

import logging

import grpc

from classifier.v1 import classifier_pb2, classifier_pb2_grpc

from .model import ClassifierModel
from .personalization import detect_personalization
from .taxonomy import SOURCE

LOGGER = logging.getLogger(__name__)
SUPPORTED_LOCALES = frozenset(("en", "th"))


def normalize_locale(locale: str) -> str:
    return locale.strip().replace("_", "-").split("-", maxsplit=1)[0].casefold()


class ClassifierService(classifier_pb2_grpc.ClassifierServiceServicer):
    def __init__(self, model: ClassifierModel) -> None:
        self._model = model

    async def Classify(self, request, context):  # noqa: N802 - generated RPC name
        question = request.question.strip()
        locale = normalize_locale(request.locale)

        if not question:
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "question is required")
        if locale not in SUPPORTED_LOCALES:
            await context.abort(
                grpc.StatusCode.INVALID_ARGUMENT,
                "locale must be English (en) or Thai (th)",
            )

        try:
            prediction = self._model.predict(question, locale)
            personalization = detect_personalization(question)
        except Exception:
            # Do not include request text in logs or exception metadata.
            LOGGER.exception("Classifier inference failed")
            await context.abort(grpc.StatusCode.INTERNAL, "classification failed")

        response = classifier_pb2.ClassifyResponse(
            domain=prediction.domain,
            intent=prediction.intent,
            confidence=prediction.confidence,
            personalization=personalization,
            source=SOURCE,
            model_version=self._model.model_version,
            decision_method=prediction.decision_method,
            embedding_model_version=prediction.embedding_model_version or "",
        )
        if prediction.semantic_similarity is not None:
            response.semantic_similarity = prediction.semantic_similarity
        return response
