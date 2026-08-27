# Tarot Destiny Python Classifier

Private CPU classification service for free-text Tarot questions. It implements the
unary `ClassifierService.Classify` RPC from
`contracts/classifier/v1/classifier.proto` and the standard gRPC health service.

The bundled TF-IDF and logistic-regression model is a seed baseline. It predicts the
fixed intent taxonomy in English and Thai; C# remains responsible for validating the
result and deciding shared-cache eligibility.

The service never logs raw question text. A reviewed model can additionally use a
locale-partitioned TF-IDF/SVD semantic embedding index to recognize similar intent;
the gRPC response reports the decision method, similarity, and embedding version.

## Local setup

Run these commands from `apps/classifier` with Python 3.11:

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
.venv\Scripts\python scripts/generate_proto.py
.venv\Scripts\python scripts/train_model.py
$env:PYTHONPATH = "src"
.venv\Scripts\python -m unittest discover -s tests -v
.venv\Scripts\python -m tarot_classifier.server
```

The server listens on port `50051`. Override the defaults with
`CLASSIFIER_PORT` and `CLASSIFIER_MODEL_PATH`.

Check a running server with:

```powershell
.venv\Scripts\python scripts/health_check.py
```

## Docker

The proto is outside this service directory, so build from the repository root:

```powershell
docker build -f apps/classifier/Dockerfile -t tarot-destiny-classifier .
docker run --rm -p 50051:50051 tarot-destiny-classifier
```

The image generates bindings and trains the versioned artifact during the build. It
runs as a non-root user and exposes a Docker health check backed by the standard gRPC
health API.

## Model lifecycle

`scripts/train_model.py` writes `artifacts/classifier.joblib`. A seed artifact contains
the fitted pipeline, fixed intent-to-domain mapping, and model version
`tfidf-logreg-seed-v1`. A reviewed artifact also contains a dense semantic index,
reviewed row count, and dataset hash; it does not retain raw reviewed rows separately.
The server rejects artifacts with an incompatible schema, version, embedding version,
or taxonomy.

To retrain after changing seed data:

```powershell
.venv\Scripts\python scripts/train_model.py --artifact artifacts/classifier.joblib
```

To train from the API's approved-only admin export:

```powershell
.venv\Scripts\python scripts/train_model.py `
  --reviewed-data reviewed-export.json `
  --artifact artifacts/classifier.joblib `
  --manifest artifacts/classifier-manifest.json
```

The export schema is `tarot-classifier-reviewed-v1`. Training validates locale,
taxonomy/domain agreement, length, duplicates, and conflicting labels before fitting.
