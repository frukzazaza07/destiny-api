# Tarot Destiny Implementation

Phase 1 MVP and optional Premium Deep readings are implemented. The runtime follows the core product rule:

```text
Rule engine decides what the cards mean.
STANDARD uses the rule engine for the finished reading.
DEEP lets qwen3:4b express a premium reading from the same authoritative rules.
```

The shared finished-answer cache is safe at the intent boundary: cache-eligible generation receives reusable domain/intent and rule context, not the raw user question. Highly personalized or low-confidence questions skip shared cache and retain their full question for generation.

## Projects

- `apps/api` - ASP.NET Core API, classifier policy/fallback, deck sessions, bilingual Tarot rules, Redis finished-answer cache, LLM client, validation, logging, and metrics.
- `apps/classifier` - private Python 3.11 gRPC service with a bilingual TF-IDF + Logistic Regression CPU model and standard gRPC health checks.
- `contracts/classifier/v1` - shared, versioned classifier protobuf contract.
- `apps/web` - Next.js reading experience with English/Thai, topic or question modes, `DESTINY_3`, and `DAILY_1`.
- `tests/TarotDestiny.Api.Tests` - discoverable MSTest acceptance suite run by `dotnet test`.
- `docker-compose.yml` - private-loopback classifier and Redis for Phase 1, with PostgreSQL ready for Phase 2.

## Local Run

Start the classifier and Redis:

```powershell
docker compose up -d --build classifier redis
```

Start the backend:

```powershell
dotnet run --project apps/api/TarotDestiny.Api.csproj --urls http://0.0.0.0:5000
```

Start the frontend in another terminal:

```powershell
cd apps/web
npm.cmd install
npm.cmd run dev
```

Open `http://localhost:3000`. The development backend connects to the classifier at `127.0.0.1:50051` and Redis at `localhost:6379`. A classifier outage uses the C# rule fallback, while a Redis outage is treated as a cache miss; neither outage blocks reading generation.

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
  -> Redis MISS: render STANDARD or call qwen3:4b for DEEP, validate, cache, return
```

Raw free-text is removed before a shared-cache LLM call. The backend also owns card identity, name, order, and orientation in the final response; generated prose cannot replace those values.

## Classifier Configuration

The Python model covers all 29 learned intents in English and Thai and returns `domain`, `intent`, `confidence`, `personalization`, `source`, and `modelVersion`. Its seed corpus is an architectural baseline; low-confidence output becomes `PERSONAL_CUSTOM` and skips shared cache until reviewed production labels are available.

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

Development is configured for the local OpenAI-compatible Ollama endpoint and `qwen3:4b`:

```powershell
$env:LLM__Endpoint = "http://127.0.0.1:11434/v1/chat/completions"
$env:LLM__Model = "qwen3:4b"
```

Only `DEEP` calls this endpoint. `STANDARD` always uses the deterministic bilingual rule renderer. A Deep request fails with a controlled backend error when the endpoint is not configured; it is not silently downgraded after payment.

The client disables Qwen thinking output, supplies a strict spread-aware JSON schema, enforces timeouts, retries, output-token limits, and maximum concurrency, and rejects malformed, wrong-language, or card-altering responses before cache storage. Keep the inference endpoint private over localhost, Tailscale, or WireGuard.

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

## Verification

```powershell
dotnet test TarotDestiny.sln
apps\classifier\.venv\Scripts\python.exe apps/classifier/scripts/generate_proto.py
apps\classifier\.venv\Scripts\python.exe -m unittest discover -s apps/classifier/tests -v
apps\classifier\.venv\Scripts\python.exe apps/classifier/scripts/health_check.py
cd apps/web
npm.cmd run lint
npm.cmd run build
npm.cmd audit --audit-level=high
```

The acceptance suites cover premium entitlement, Standard/Deep cache separation, zero-LLM Standard generation, classifier taxonomy/model persistence/gRPC health, C# routing and fallback policy, all cache-key dimensions, Deep cache hit and LLM avoidance, raw-question isolation, personalization bypass, malformed and wrong-language LLM JSON, card validation, concurrency, Redis disconnection, deck integrity, idempotent reveals, and bilingual master data.

## Phase 2

The following remain intentionally deferred according to the approved plan:

- EF Core/Npgsql generated-answer persistence;
- Redis miss lookup in PostgreSQL and Redis repopulation;
- persistent `hit_count` and cache analytics;
- distributed `tarot:lock:<hash>` stampede protection with double-check;
- classifier training records and human review workflow.

Existing boundaries for Phase 2 are `IAnswerCache`, `ICacheKeyBuilder`, `ITarotReadingService`, `ILlmClient`, and the versioned cache identity.
