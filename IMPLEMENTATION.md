# Tarot Destiny Implementation

Phase 1 MVP and optional Premium Deep readings are implemented. The runtime follows the core product rule:

```text
Rule engine decides what the cards mean.
STANDARD uses the rule engine for the finished reading.
DEEP lets qwen3:8b express a premium reading from the same authoritative rules.
```

The shared finished-answer cache is safe at the intent boundary: cache-eligible generation receives reusable domain/intent and rule context, not the raw user question. Highly personalized or low-confidence questions skip shared cache and retain their full question for generation.

## Projects

- `apps/api` - ASP.NET Core API, classifier policy/fallback, deck sessions, bilingual Tarot rules, Redis finished-answer cache, PostgreSQL generated-answer library, LLM client, validation, logging, and metrics.
- `apps/classifier` - private Python 3.11 gRPC service with a bilingual TF-IDF + Logistic Regression CPU model, reviewed-data training, latent-semantic embeddings, and standard gRPC health checks.
- `contracts/classifier/v1` - shared, versioned classifier protobuf contract.
- `apps/web` - Next.js reading experience with English/Thai, topic or question modes, `DESTINY_3`, and `DAILY_1`.
- `tests/TarotDestiny.Api.Tests` - discoverable MSTest acceptance suite run by `dotnet test`.
- `docker-compose.infra.yml` - independently managed Nginx, PostgreSQL, Redis, RabbitMQ, persistent data storage, and shared edge/backend networks.
- `docker-compose.yml` - application services with health-gated startup, migrations, external shared networks, persistent data, and optional GPU profiles.

## Local Run

Copy the development environment template, review its development-only credentials, and start the complete platform. Replace every credential before a shared or production deployment:

```powershell
Copy-Item .env.example .env
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml up -d --wait
docker compose up --build
```

Start infrastructure first and wait for PostgreSQL, Redis, and RabbitMQ to be healthy. The app builds Next.js, ASP.NET, and the classifier; applies committed EF migrations in the one-shot `migrate` service; and starts API/web after their app dependencies are ready. See [INFRASTRUCTURE.md](INFRASTRUCTURE.md) for resource names and the one-time migration of an existing combined deployment.

The checked-in Nginx configuration serves `https://dooduang.cc` and requires its TLS certificates. It publishes ports 80/443; API port 5000 and web port 3000 are container-only. Development Swagger is available at `/swagger/index.html` and `/swagger/v1/swagger.json` through an operator-accessible API endpoint, while public Nginx hides it. Redis retains its configured host binding. Named volumes retain database data, Redis data, classifier artifacts, and downloaded Ollama models.

Start local Ollama with NVIDIA GPU access and pull the models listed in `OLLAMA_MODELS`:

```powershell
docker compose --profile local-gpu up --build
```

For an externally hosted private GPU, set `TAROT_LLM_*_ENDPOINT`, `TAROT_EXTERNAL_LLM_HEALTH_URL`, and any required cloud-fallback settings in `.env`, then run:

```powershell
docker compose --profile external-gpu up --build
```

The external profile runs a one-shot reachability check without publishing the inference endpoint. A classifier outage uses the C# rule fallback, while a Redis outage falls through to PostgreSQL and generation; neither outage blocks reading generation after startup.

## Runtime Flow

```text
Question or topic
  -> explicit topic: deterministic C# classification
  -> free text: Python CPU classifier over private unary gRPC
  -> validate taxonomy + apply C# personalization guard
  -> timeout/unavailable/invalid response: C# rule fallback
  -> STANDARD: rule renderer, zero LLM calls
  -> DEEP: verify premium entitlement
  -> shared-cache eligibility
  -> SHA-256 key from mode + intent + spread + locale + ordered cards + versions
  -> Redis HIT: return finished structured answer; avoid Ollama for DEEP
  -> Redis MISS: read the persistent PostgreSQL library and repopulate Redis on HIT
  -> persistent MISS: acquire tarot:lock:<hash>, double-check, then generate once
  -> STANDARD: render rules; DEEP: route to the selected tier/worker and validate quality
  -> persist one or more numbered variants and increment hit_count on reuse
  -> cache in Redis and return
```

Raw free-text is removed before a shared-cache LLM call. The backend also owns card identity, name, order, and orientation in the final response; generated prose cannot replace those values.

Only shared-cache-eligible misses are persisted. Personalized readings are not written to the shared answer library. PostgreSQL reads and writes are best effort: an outage is logged and reading generation continues. Applying migrations is an explicit deployment step so an unavailable optional database cannot prevent the API process from starting.

The answer library stores one identity row plus numbered variant rows. Redis contains the same variant set and randomly selects a variant for normal hits. The offline warmer can request a missing variant explicitly without changing cache identity.

## Classifier Configuration

The Python model covers all 29 learned intents in English and Thai and returns classification, model, decision-method, semantic-similarity, and embedding-version metadata. Reviewed examples train a versioned custom model and a CPU TF-IDF/SVD semantic index. Strong semantic neighbors can select a reusable intent; conflicts remain `PERSONAL_CUSTOM`.

```json
{
  "Classifier": {
    "MinimumCacheConfidence": 0.85,
    "UseGrpc": true,
    "GrpcAddress": "http://127.0.0.1:50051",
    "DeadlineMilliseconds": 500
  }
}
```

Use `Classifier__GrpcAddress` to override the endpoint. Keep it private in production. The API response reports `PYTHON_GRPC`, `CSHARP_TOPIC`, `CSHARP_RULE`, or `CSHARP_RULE_FALLBACK` so runtime routing is observable without logging raw questions.

## API

- `GET /swagger` - interactive Swagger UI in Development.
- `GET /swagger/v1/swagger.json` - generated OpenAPI document in Development.
- `GET /health` - service health.
- `GET /metrics` - reading, classifier, cache, LLM-call, avoidance, failure, and latency counters.
- `GET /api/readings/options` - available reading modes and current Deep entitlement.
- `POST /api/deck/shuffle` - creates a cryptographically shuffled 78-card session.
- `POST /api/deck/{sessionId}/resolve` - resolves one strict, idempotent card selection; sessions expire after 15 minutes.
- `POST /api/readings/generate` - classifies, caches, interprets, generates, validates, and returns a structured reading.
- `POST /api/classifier/training-examples` - accepts an explicitly consented, privacy-screened training question.
- `GET/PUT /api/classifier/training-examples...` - admin-key protected queue, review, taxonomy, and approved export endpoints.
- `GET /api/admin/cache/analytics` - admin-key protected runtime/persistent cache analytics.
- `POST/GET /api/admin/cache/warmups...` - admin-key protected bounded one-card warmup jobs.

The operator UI is at `http://localhost:3000/admin`. Its admin key is kept in browser `sessionStorage`; configure `ADMIN_KEY`/`Admin__Key` outside source control in production. Compose's fallback development value is `tarot-development-admin`; `.env.example` intentionally asks you to replace it.

Supported values:

- Locales: `en`, `th`
- Spreads: `DESTINY_3`, `DAILY_1`
- Orientations: `UPRIGHT`, `REVERSED`
- Reading modes: `STANDARD`, `DEEP`

## Tarot Rules

All 78 cards have explicit English and Thai names, upright meanings, reversed meanings, and keywords. The interpretation payload adds:

- spread-position meaning;
- domain lens;
- dominant element;
- reversed-card pattern;
- Major Arcana weight;
- card-to-card transition;
- overall narrative, opportunity, challenge, guidance, and reflection question.

## LLM Configuration

Development defines `CORE` (`qwen3:8b`), `ADVANCED` (`qwen3:14b`), and `PREMIUM` (`qwen3:32b`) tiers. Each tier can contain multiple priority-ordered local workers and cloud workers:

```powershell
$env:LLM__Endpoint = "http://127.0.0.1:11434/v1/chat/completions"
$env:LLM__Model = "qwen3:8b"
$env:LLM__EnableCloudFallback = "false"
```

Only `DEEP` calls this endpoint. `STANDARD` always uses the deterministic bilingual rule renderer. A Deep request fails with a controlled backend error when the endpoint is not configured; it is not silently downgraded after payment.

The client disables Qwen thinking output, supplies a strict spread-aware JSON schema, enforces per-worker concurrency, timeouts, retries, circuit breaking and failover, and rejects malformed, wrong-language, card-altering, or below-threshold responses before cache storage. Cloud fallback is opt-in and raw personalized questions remain local unless separately permitted. Stable weighted prompt experiments are assigned from reusable identity only and included in the cache namespace.

The deterministic interpretation payload has a separate bounded in-memory cache keyed by interpretation version, intent, spread, locale, positions, cards, and orientations. It excludes raw question wording and final prose.

## Premium Entitlement

Development allows Deep mode without authentication for local testing. Production requires an authenticated `tarot:deep_reading=true` claim. Configure the future checkout or subscription service to grant that claim after confirmed payment; the browser request itself is never proof of payment.

```json
{
  "DeepReading": {
    "Enabled": true,
    "AllowUnentitledInDevelopment": false,
    "ClaimType": "tarot:deep_reading",
    "ClaimValue": "true",
    "UpgradeUrl": null
  }
}
```

For production Redis, set `Redis__ConnectionString` or `ConnectionStrings__Redis`. If neither is configured, the API uses the in-memory cache for isolated development.

For PostgreSQL, set `Postgres__ConnectionString` or `ConnectionStrings__Postgres`, apply the EF migration during deployment, and keep the database private. If neither connection is configured, generated-answer persistence uses a no-op store and reading generation remains available.

## Verification

```powershell
dotnet test TarotDestiny.sln
dotnet ef migrations has-pending-model-changes --project apps/api/TarotDestiny.Api.csproj --startup-project apps/api/TarotDestiny.Api.csproj -- --environment Development
apps\classifier\.venv\Scripts\python.exe apps/classifier/scripts/generate_proto.py
apps\classifier\.venv\Scripts\python.exe -m unittest discover -s apps/classifier/tests -v
apps\classifier\.venv\Scripts\python.exe apps/classifier/scripts/health_check.py
cd apps/web
npm.cmd run lint
npm.cmd run build
npm.cmd audit --audit-level=high
cd ../..
docker compose config --quiet
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml config --quiet
docker compose --profile local-gpu config --quiet
docker compose --profile external-gpu config --quiet
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml up --detach --wait
docker compose up --detach --build --wait
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-compose.ps1 -AdminKey "<ADMIN_KEY from .env>"
```

The Compose smoke script verifies the web, API health envelope, generated OpenAPI document, Swagger UI, classifier health, PostgreSQL migration table, Redis, a cache miss followed by a cache hit, stable cache identity, and cache analytics.

The acceptance suites cover premium entitlement, Standard/Deep/tier/experiment cache separation, Redis/PostgreSQL read-through, single-flight generation, variants, base interpretation reuse, consent privacy rules, classifier taxonomy/model/semantic persistence and gRPC health, routing and fallback policy, raw-question isolation, response quality, malformed and wrong-language LLM JSON, card validation, concurrency, Redis disconnection, deck integrity, idempotent reveals, OpenAPI exposure, and bilingual master data.

## Operations and model lifecycle

- Run bounded one-card warming with `scripts/warm-one-card-cache.ps1`; use offsets to process the complete 9,048-combination bilingual space per reading mode.
- Review consented examples at `/admin`, then download the approved export endpoint.
- Train a reviewed classifier with `apps/classifier/scripts/train_model.py --reviewed-data <export.json> --manifest <manifest.json>`.
- The artifact persists fitted vectorizers, the dense semantic index, labels, locale partitions, version/hash metadata, and no raw reviewed rows.
- Cache analytics expose aggregate counts and hashed answer identities, never raw questions or generated prose.
