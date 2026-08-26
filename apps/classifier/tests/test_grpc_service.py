from __future__ import annotations

from pathlib import Path
import sys
import unittest

import grpc
from grpc_health.v1 import health_pb2, health_pb2_grpc


CLASSIFIER_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CLASSIFIER_ROOT / "src"))

from classifier.v1 import classifier_pb2, classifier_pb2_grpc  # noqa: E402
from tarot_classifier.model import ClassifierModel  # noqa: E402
from tarot_classifier.server import SERVICE_NAME, create_server  # noqa: E402
from tarot_classifier.taxonomy import MODEL_VERSION, SOURCE  # noqa: E402


class GrpcServiceTests(unittest.IsolatedAsyncioTestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.model = ClassifierModel.train()

    async def asyncSetUp(self) -> None:
        self.server, _, port = await create_server(self.model, "127.0.0.1:0")
        await self.server.start()
        self.channel = grpc.aio.insecure_channel(f"127.0.0.1:{port}")

    async def asyncTearDown(self) -> None:
        await self.channel.close()
        await self.server.stop(grace=None)

    async def test_classify_returns_contract_metadata(self) -> None:
        stub = classifier_pb2_grpc.ClassifierServiceStub(self.channel)
        response = await stub.Classify(
            classifier_pb2.ClassifyRequest(
                question="Should I change my job?",
                locale="en",
            )
        )

        self.assertEqual("CAREER", response.domain)
        self.assertEqual("CAREER_CHANGE_JOB", response.intent)
        self.assertGreaterEqual(response.confidence, 0.85)
        self.assertEqual("LOW", response.personalization)
        self.assertEqual(SOURCE, response.source)
        self.assertEqual(MODEL_VERSION, response.model_version)

    async def test_health_service_reports_serving(self) -> None:
        health_stub = health_pb2_grpc.HealthStub(self.channel)
        overall = await health_stub.Check(health_pb2.HealthCheckRequest(service=""))
        classifier = await health_stub.Check(
            health_pb2.HealthCheckRequest(service=SERVICE_NAME)
        )
        self.assertEqual(health_pb2.HealthCheckResponse.SERVING, overall.status)
        self.assertEqual(health_pb2.HealthCheckResponse.SERVING, classifier.status)

    async def test_invalid_request_returns_invalid_argument(self) -> None:
        stub = classifier_pb2_grpc.ClassifierServiceStub(self.channel)
        with self.assertRaises(grpc.aio.AioRpcError) as raised:
            await stub.Classify(
                classifier_pb2.ClassifyRequest(question="", locale="en")
            )
        self.assertEqual(grpc.StatusCode.INVALID_ARGUMENT, raised.exception.code())


if __name__ == "__main__":
    unittest.main()
