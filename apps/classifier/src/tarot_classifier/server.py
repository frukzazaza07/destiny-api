"""Tarot Destiny asynchronous gRPC classifier server."""

from __future__ import annotations

import asyncio
import logging
import os
from pathlib import Path

import grpc
from grpc_health.v1 import health, health_pb2, health_pb2_grpc

from classifier.v1 import classifier_pb2_grpc

from .model import ClassifierModel
from .service import ClassifierService

LOGGER = logging.getLogger(__name__)
SERVICE_NAME = "tarotdestiny.classifier.v1.ClassifierService"
DEFAULT_ARTIFACT = Path(__file__).resolve().parents[2] / "artifacts" / "classifier.joblib"


async def create_server(
    model: ClassifierModel,
    bind_address: str,
) -> tuple[grpc.aio.Server, health.aio.HealthServicer, int]:
    server = grpc.aio.server(
        options=(
            ("grpc.max_receive_message_length", 64 * 1024),
            ("grpc.max_send_message_length", 64 * 1024),
        )
    )
    classifier_pb2_grpc.add_ClassifierServiceServicer_to_server(
        ClassifierService(model), server
    )
    health_servicer = health.aio.HealthServicer()
    health_pb2_grpc.add_HealthServicer_to_server(health_servicer, server)

    port = server.add_insecure_port(bind_address)
    if port == 0:
        raise RuntimeError(f"Unable to bind gRPC server to {bind_address}")

    await health_servicer.set("", health_pb2.HealthCheckResponse.SERVING)
    await health_servicer.set(SERVICE_NAME, health_pb2.HealthCheckResponse.SERVING)
    return server, health_servicer, port


async def serve() -> None:
    artifact_path = Path(os.getenv("CLASSIFIER_MODEL_PATH", str(DEFAULT_ARTIFACT)))
    port = int(os.getenv("CLASSIFIER_PORT", "50051"))
    model = ClassifierModel.load(artifact_path)
    server, health_servicer, _ = await create_server(model, f"[::]:{port}")

    await server.start()
    LOGGER.info(
        "Classifier service started on port %d with model %s",
        port,
        model.model_version,
    )

    try:
        await server.wait_for_termination()
    finally:
        await health_servicer.set("", health_pb2.HealthCheckResponse.NOT_SERVING)
        await health_servicer.set(
            SERVICE_NAME, health_pb2.HealthCheckResponse.NOT_SERVING
        )
        await server.stop(grace=5)


def main() -> None:
    logging.basicConfig(
        level=os.getenv("LOG_LEVEL", "INFO").upper(),
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )
    asyncio.run(serve())


if __name__ == "__main__":
    main()
