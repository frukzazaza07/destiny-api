FROM python:3.11-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PYTHONPATH=/workspace/apps/classifier/src \
    CLASSIFIER_MODEL_PATH=/workspace/apps/classifier/artifacts/classifier.joblib \
    CLASSIFIER_PORT=50051

WORKDIR /workspace

COPY apps/classifier/requirements.txt /workspace/apps/classifier/requirements.txt
RUN python -m pip install --no-cache-dir -r /workspace/apps/classifier/requirements.txt

COPY contracts/classifier/v1/classifier.proto /workspace/contracts/classifier/v1/classifier.proto
COPY apps/classifier /workspace/apps/classifier

WORKDIR /workspace/apps/classifier
RUN python scripts/generate_proto.py \
    --proto-root /workspace/contracts \
    --output-root /workspace/apps/classifier/src \
    && python scripts/train_model.py \
    --artifact /workspace/apps/classifier/artifacts/classifier.joblib \
    && addgroup --system classifier \
    && adduser --system --ingroup classifier classifier \
    && chown -R classifier:classifier /workspace/apps/classifier

USER classifier

EXPOSE 50051

HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
    CMD ["python", "scripts/health_check.py"]

CMD ["python", "-m", "tarot_classifier.server"]
