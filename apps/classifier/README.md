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

The Compose artifact volume is versioned with the classifier artifact schema
(`classifier-artifacts-v3`). When the artifact schema changes, use a new volume name
instead of mounting an incompatible older model over the model baked into the image.
The old named volume remains available for inspection or rollback and is not deleted
automatically.

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

The dataset schema is `tarot-classifier-dataset-v2`. It records domain, intent,
personalization, source provenance, review status, paraphrase group, audit timestamps,
optional prediction/reviewer metadata, split, and evaluation tags. Only `APPROVED`
rows enter training or evaluation; synthetic bootstrap rows remain `PENDING` until a
human reviews them.

Build the deterministic bilingual bootstrap corpus (11,836 rows) with:

```powershell
.venv\Scripts\python scripts/build_bootstrap_dataset.py
```

The generated `datasets/bootstrap-v2.json` contains reviewed seed rows plus expansion
candidates and 1,000 hard negatives. Its pending rows are coverage work items, not
production truth. Training uses paraphrase-group 70/15/15 splits and calibrates on the
held-out validation partition only when every learned class has validation coverage.

Generate the untouched final-test report and optionally fail CI when the strict gate
does not pass:

```powershell
.venv\Scripts\python scripts/evaluate_model.py `
  --artifact artifacts/classifier-candidate-v2.joblib `
  --dataset datasets/bootstrap-v2.json `
  --report artifacts/classifier-candidate-v2-report.json `
  --approved-intent CAREER_CHANGE_JOB `
  --require-quality-gate
```

The report includes strict `> 0.90` accepted precision and coverage by intent and
locale, required language/boundary/rejection subsets, calibration error, confusion
pairs, rejection rate, and a question-free false-high-confidence review list. Do not
enable shared reads until the reviewed dataset targets and this quality gate pass.
