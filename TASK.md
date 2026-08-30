# TASK.md — Tarot LLM Classification + Finished Answer Cache

## Objective

Implement a caching architecture that reduces LLM GPU usage by reusing a previously generated Tarot reading when:

- The user's question is classified into the same question type / intent
- The Tarot spread is the same
- The selected cards are the same
- Card positions are the same
- Card orientations are the same
- Language is the same
- Prompt / interpretation version is the same

The system should classify the user's question before calling the LLM.

If a matching finished answer already exists in cache, return it immediately.

If no matching answer exists, generate by reading mode, validate the result, store it in cache, and return it. `STANDARD` renders through the rule engine; only entitled `DEEP` requests call the LLM.

---

# High-Level Flow

```text
User Question
     ↓
Question Classifier
     ↓
Classification Result
     ↓
Can result use shared cache?
     │
     ├── NO → Generate directly by reading mode
     │
     └── YES
            ↓
       Build Cache Key
            ↓
          Redis
        ↙       ↘
      HIT       MISS
       ↓          ↓
Return Cache   Generate by mode
                  ↓
             Validate Result
                  ↓
              Save Cache
                  ↓
               Return
```

---

# Core Principle

The expensive GPU should be used primarily for actual Tarot response generation.

Question classification should use:

- CPU
- Small ML model
- Lightweight classifier
- Or rules initially

Avoid using the main LLM GPU for simple classification unless necessary.

---

# Phase 1 — Define Question Taxonomy

Create a fixed taxonomy of supported domains and intents.

Do not make the taxonomy too broad or too specific.

Recommended initial size:

```text
20–30 intents
```

## Example Domains

```text
GENERAL
LOVE
CAREER
MONEY
FAMILY
PERSONAL_GROWTH
```

## Example Career Intents

```text
CAREER_GENERAL
CAREER_NEW_JOB
CAREER_CHANGE_JOB
CAREER_PROMOTION
CAREER_BUSINESS
CAREER_DECISION
CAREER_CONFLICT
```

## Example Love Intents

```text
LOVE_GENERAL
LOVE_SINGLE
LOVE_RELATIONSHIP
LOVE_BREAKUP
LOVE_RECONCILIATION
LOVE_NEW_PERSON
LOVE_COMMITMENT
LOVE_DECISION
```

## Example Money Intents

```text
MONEY_GENERAL
MONEY_INCOME
MONEY_INVESTMENT
MONEY_BUSINESS
MONEY_DEBT
MONEY_PURCHASE
MONEY_DECISION
```

## Special Intent

Always support:

```text
PERSONAL_CUSTOM
```

Use this when the question contains too much unique personal context or cannot safely reuse a generic cached answer.

---

# Phase 2 — Classification Contract

Create a classifier that accepts:

```json
{
  "question": "Should I leave my current company?",
  "locale": "en"
}
```

Expected output:

```json
{
  "domain": "CAREER",
  "intent": "CAREER_CHANGE_JOB",
  "confidence": 0.94,
  "personalization": "LOW",
  "source": "PYTHON_GRPC",
  "modelVersion": "tfidf-logreg-seed-v1"
}
```

## Required Fields

```text
domain
intent
confidence
personalization
source
modelVersion
```

## Personalization Values

```text
LOW
MEDIUM
HIGH
```

Recommended behavior:

```text
LOW
→ Eligible for finished-answer cache

MEDIUM
→ Cache may be used only if classification confidence is high

HIGH
→ Skip shared finished-answer cache and generate personalized answer
```

---

## Runtime Boundary

Use a shared protobuf contract and unary gRPC call:

```text
C# ASP.NET backend
    -> Python classifier service
    -> TF-IDF + Logistic Regression on CPU
```

Python responsibilities:

- Normalize free-text input for model inference
- Load and version the model artifact
- Return domain, intent, confidence, personalization, and model version
- Expose gRPC health status

C# responsibilities:

- Keep predefined topics deterministic and local
- Validate returned domain and intent against the fixed taxonomy
- Apply the configured cache-confidence threshold
- Raise personalization when local safety rules are stricter
- Fall back safely on timeout, unavailable service, or invalid response
- Own all cache eligibility and cache-key behavior

The gRPC classifier must be private and must not be exposed through the public edge.

---

# Phase 3 — Confidence Rules

Recommended initial thresholds:

```text
confidence >= 0.85
→ classification accepted

confidence < 0.85
→ classify as PERSONAL_CUSTOM
```

Do not reuse a cached answer if the classifier is uncertain.

Example:

```json
{
  "domain": "CAREER",
  "intent": "CAREER_CHANGE_JOB",
  "confidence": 0.61,
  "personalization": "HIGH"
}
```

Result:

```text
Skip shared cache
Generate directly by reading mode
```

Threshold must be configurable.

Example:

```json
{
  "Classifier": {
    "MinimumCacheConfidence": 0.85
  }
}
```

---

# Phase 4 — Detect Highly Personalized Questions

Questions containing detailed personal context should not reuse generic final responses.

Example:

```text
I have worked with my brother for 8 years and we are considering
selling our company because he wants to move overseas.
What should I do?
```

Possible classification:

```json
{
  "domain": "CAREER",
  "intent": "CAREER_BUSINESS",
  "confidence": 0.92,
  "personalization": "HIGH"
}
```

Behavior:

```text
Do not use shared finished-answer cache.
```

Possible factors for HIGH personalization:

- Multiple named people
- Detailed personal history
- Detailed company/business context
- Specific financial numbers
- Multiple life domains in one question
- Detailed timeline
- Unique personal circumstances
- Long free-form question

---

# Phase 5 — Reading Input Contract

Example request:

```json
{
  "question": "Should I change my job?",
  "readingMode": "DEEP",
  "spread": "DESTINY_3",
  "locale": "th",
  "cards": [
    {
      "position": "PAST",
      "cardId": "THE_TOWER",
      "orientation": "UPRIGHT"
    },
    {
      "position": "PRESENT",
      "cardId": "THE_MAGICIAN",
      "orientation": "UPRIGHT"
    },
    {
      "position": "DIRECTION",
      "cardId": "THE_STAR",
      "orientation": "UPRIGHT"
    }
  ]
}
```

The card list must preserve spread position.

## Standard And Paid Deep Modes

```text
STANDARD
→ Render from the deterministic rule payload
→ Never call the LLM

DEEP
→ Require backend-verified premium entitlement
→ Call private Ollama qwen3:8b only on an eligible cache miss
```

The request field defaults to `STANDARD` for backward compatibility:

```json
{
  "readingMode": "STANDARD"
}
```

Production entitlement is represented by the authenticated claim:

```text
tarot:deep_reading=true
```

Never accept `isPaid`, `premium`, or an equivalent client-controlled boolean as authorization. The future payment provider must grant the authenticated server-side claim after confirmed payment or subscription activation.

---

# Phase 6 — Cache Key Design

Cache key must include:

```text
cache version
question domain
question intent
reading mode
spread
locale
position
card
orientation
prompt version
interpretation version
```

Required for `DEEP`:

```text
model version
```

Do not use user ID in the shared cache key.

If user ID is included, almost every request becomes unique and cache reuse becomes useless.

---

# Canonical Cache Input

Build a deterministic string such as:

```text
v1
|CAREER
|CAREER_CHANGE_JOB
|DEEP
|DESTINY_3
|TH
|PAST:THE_TOWER:UPRIGHT
|PRESENT:THE_MAGICIAN:UPRIGHT
|DIRECTION:THE_STAR:UPRIGHT
|PROMPT_V3
|INTERPRETATION_V2
```

Then hash it:

```text
SHA256(canonicalString)
```

Redis key:

```text
tarot:answer:<sha256>
```

Example:

```text
tarot:answer:7d231c7d...
```

---

# Important: Card Order Must Matter

These readings must create different cache keys.

Reading A:

```text
PAST      = THE_TOWER
PRESENT   = THE_MAGICIAN
DIRECTION = THE_STAR
```

Reading B:

```text
PAST      = THE_STAR
PRESENT   = THE_MAGICIAN
DIRECTION = THE_TOWER
```

Even though the cards are identical as a set, their Tarot meanings differ because their spread positions differ.

Do not sort cards alphabetically before building the cache key.

Use:

```text
position + cardId + orientation
```

---

# Phase 7 — Prompt Versioning

Prompt version must be included in the cache key.

Example:

```text
PROMPT_V1
PROMPT_V2
PROMPT_V3
```

Reason:

If the system prompt changes, old cached answers may no longer match the new desired output.

Instead of deleting all old cache entries, changing:

```text
PROMPT_V3
```

to:

```text
PROMPT_V4
```

automatically creates a new cache namespace.

---

# Phase 8 — Interpretation Versioning

Tarot card meaning data may also change.

Include:

```text
INTERPRETATION_V1
```

in the cache key.

If Tarot master content is updated:

```text
INTERPRETATION_V2
```

New readings will automatically stop using old cached answers.

---

# Phase 9 — Finished LLM Response Format

Cache structured JSON instead of raw text.

Example:

```json
{
  "title": "A New Direction Is Opening",
  "summary": "Your reading suggests...",
  "mainTheme": "Transformation and rebuilding",
  "cards": [
    {
      "position": "PAST",
      "cardId": "THE_TOWER",
      "interpretation": "..."
    },
    {
      "position": "PRESENT",
      "cardId": "THE_MAGICIAN",
      "interpretation": "..."
    },
    {
      "position": "DIRECTION",
      "cardId": "THE_STAR",
      "interpretation": "..."
    }
  ],
  "opportunities": [
    "..."
  ],
  "challenges": [
    "..."
  ],
  "guidance": [
    "..."
  ],
  "reflectionQuestion": "...",
  "closingMessage": "..."
}
```

Benefits:

- Consistent frontend rendering
- Easier validation
- Easier localization
- Easier future response redesign
- Easier cache migration

---

# Phase 10 — Redis Cache Behavior

Pseudo flow:

```text
classification = Classify(question)

if classification.personalization == HIGH or classification.confidence < threshold:
    return GenerateByMode(reading, classification)

cacheKey = BuildCacheKey(classification, reading)

cached = Redis.GET(cacheKey)

if cached exists:
    return cached

result = GenerateByMode(reading, classification)

Validate(result)

Redis.SET(cacheKey, result)

return result
```

---

# Example C# Pseudocode

```csharp
public async Task<TarotReadingResponse> GenerateReadingAsync(
    TarotReadingRequest request,
    CancellationToken cancellationToken)
{
    var classification = await _questionClassifier.ClassifyAsync(
        request.Question,
        request.Locale,
        cancellationToken);

    var canUseSharedCache =
        classification.Confidence >= _options.MinimumCacheConfidence &&
        classification.Personalization != PersonalizationLevel.High &&
        classification.Intent != "PERSONAL_CUSTOM";

    if (!canUseSharedCache)
    {
        return await GenerateByModeAsync(
            request,
            classification,
            cancellationToken);
    }

    var cacheKey = _cacheKeyBuilder.Build(
        request,
        classification);

    var cached = await _cache.GetAsync<TarotReadingResponse>(
        cacheKey,
        cancellationToken);

    if (cached is not null)
    {
        return cached;
    }

    var result = await GenerateByModeAsync(
        request,
        classification,
        cancellationToken);

    await _cache.SetAsync(
        cacheKey,
        result,
        _options.AnswerCacheTtl,
        cancellationToken);

    return result;
}
```

---

# Phase 11 — Cache TTL

Initial recommendation:

```text
Finished generic Tarot reading:
7–30 days
```

Because the output is already versioned by:

```text
prompt version
interpretation version
locale
intent
cards
spread
```

Long TTL is acceptable.

Possible configuration:

```json
{
  "TarotCache": {
    "AnswerTtlDays": 30
  }
}
```

Later, consider no TTL and rely entirely on versioned keys if storage usage is acceptable.

---

# Phase 12 — Prevent Cache Stampede

Problem:

Many users request the same uncached reading simultaneously.

Example:

```text
100 requests
same cache key
same time
```

Without protection:

```text
100 cache misses
→ 100 LLM requests
```

This wastes GPU.

Implement distributed locking.

Flow:

```text
Cache MISS
    ↓
Acquire lock for cache key
    ↓
Check cache again
    ↓
If another request filled cache
    → return cache

Otherwise
    ↓
Call LLM once
    ↓
Save result
    ↓
Release lock
```

Redis lock example key:

```text
tarot:lock:<sha256>
```

Recommended lock timeout:

```text
30–120 seconds
```

depending on maximum LLM generation time.

---

# Phase 13 — Store Multiple Variants

Optional optimization.

Instead of storing one answer:

```text
cache key
→ one finished answer
```

support:

```text
cache key
→ multiple variants
```

Example:

```json
{
  "variants": [
    {
      "variantId": 1,
      "response": {}
    },
    {
      "variantId": 2,
      "response": {}
    },
    {
      "variantId": 3,
      "response": {}
    }
  ]
}
```

Runtime:

```text
Cache HIT
    ↓
Randomly select one variant
    ↓
Return
```

Benefits:

- No additional LLM cost
- Different users do not always see identical wording
- Better perceived personalization

This is optional for MVP.

---

# Phase 14 — One-Card Pre-Generation

One-card readings are highly cacheable.

Assuming:

```text
78 cards
× 2 orientations
× 25 intents
× 2 languages
```

Total:

```text
7,800 combinations
```

This is small enough to pre-generate offline.

Possible future job:

```text
Generate all common 1-card responses
    ↓
Validate
    ↓
Save to PostgreSQL / Redis
```

Then one-card reading runtime may require zero LLM calls.

---

# Phase 15 — Three-Card Lazy Cache

Do not pre-generate every possible 3-card combination.

Number of combinations becomes very large.

Use lazy generation:

```text
First request
    ↓
MISS
    ↓
LLM
    ↓
Cache

Future same request type + cards
    ↓
HIT
```

The cache naturally grows based on real traffic.

---

# Phase 16 — Database Persistence

Do not rely only on Redis if generated responses are valuable assets.

Recommended:

```text
Redis
= fast runtime cache

PostgreSQL
= persistent generated reading library
```

Possible table:

```sql
CREATE TABLE tarot_generated_answer (
    id uuid PRIMARY KEY,
    cache_hash varchar(64) NOT NULL UNIQUE,

    domain varchar(50) NOT NULL,
    intent varchar(100) NOT NULL,

    spread_id varchar(50) NOT NULL,
    locale varchar(10) NOT NULL,

    cards jsonb NOT NULL,

    prompt_version varchar(50) NOT NULL,
    interpretation_version varchar(50) NOT NULL,
    model_version varchar(100),

    response jsonb NOT NULL,

    hit_count bigint NOT NULL DEFAULT 0,

    created_at timestamp NOT NULL DEFAULT now(),
    updated_at timestamp NULL
);
```

Flow:

```text
Redis MISS
     ↓
Optional PostgreSQL lookup
     ↓
If found
    → populate Redis
    → return

If not found
    → LLM
    → save PostgreSQL
    → save Redis
    → return
```

This prevents losing the generated library if Redis is flushed.

---

# Phase 17 — Cache Hit Metrics

Track:

```text
Total reading requests
Classifier accepted
Classifier rejected
Shared-cache eligible
Cache HIT
Cache MISS
LLM requests
LLM failures
Average LLM latency
Average cache latency
GPU utilization
Tokens generated
```

Important metric:

```text
Cache Hit Rate =
Cache Hits / Cache Eligible Requests
```

Do not calculate against all requests if some are intentionally personalized.

Example:

```text
Total requests             = 10,000
Cache eligible             = 8,000
Cache hits                 = 4,800

Cache hit rate             = 60%
Overall LLM avoidance      = 48%
```

---

# Phase 18 — Hit Counter

Increment hit count whenever a cached reading is reused.

Example:

```text
tarot_generated_answer.hit_count
```

This helps identify:

- Most common intents
- Most common card combinations
- Valuable pre-generation opportunities
- Cache storage priorities

---

# Phase 19 — Classifier Training Strategy

Do not train a custom classifier immediately unless training data already exists.

Recommended progression:

## Stage 1

Use manually defined rules or a small existing text classifier.

## Stage 2

Collect real questions:

```text
question
predicted_domain
predicted_intent
confidence
human_reviewed_label
```

## Stage 3

After enough examples exist, train a custom classifier.

Possible dataset target:

```text
Several hundred examples per important intent
```

More is preferable.

---

# Training Data Table

Example:

```sql
CREATE TABLE tarot_question_classification_training (
    id uuid PRIMARY KEY,

    question text NOT NULL,
    locale varchar(10),

    predicted_domain varchar(50),
    predicted_intent varchar(100),
    predicted_confidence numeric,

    reviewed_domain varchar(50),
    reviewed_intent varchar(100),

    is_reviewed boolean NOT NULL DEFAULT false,

    created_at timestamp NOT NULL DEFAULT now()
);
```

Do not automatically store sensitive or unnecessary user information.

Store only what is required for improving the classifier and follow applicable privacy requirements.

---

# Phase 20 — Classifier Model Goal

The classifier does not need to generate text.

Input:

```text
Should I leave my current job?
```

Output:

```text
CAREER
CAREER_CHANGE_JOB
0.94
LOW
```

This makes a small CPU model suitable.

The classifier should be optimized for:

```text
low latency
high precision
small memory usage
CPU inference
```

---

# Phase 21 — Fallback Logic

If classifier fails:

```text
Use C# rule fallback
    -> if still uncertain: PERSONAL_CUSTOM
    -> skip shared cache
```

If Redis fails:

```text
Continue to LLM
```

If PostgreSQL cache fails:

```text
Continue to LLM
```

If LLM fails:

Possible response:

```text
Retry once
    ↓
Fallback smaller model
    ↓
Return controlled error
```

Do not make cache infrastructure a hard dependency for generating readings.

---

# Phase 22 — LLM Concurrency

Protect the local GPU.

Recommended:

```text
Backend
    ↓
LLM concurrency limiter
    ↓
Queue
    ↓
GPU Server
```

Initial controls:

```text
Maximum concurrent LLM requests
Maximum queue depth
Timeout
Maximum output tokens
Retry count
```

Example:

```json
{
  "LLM": {
    "MaxConcurrency": 4,
    "TimeoutSeconds": 90,
    "MaxOutputTokens": 5000
  }
}
```

Actual concurrency must be tuned based on GPU and model.

---

# Phase 23 — Security

GPU LLM server should remain private.

Recommended:

```text
Backend VPS
    ↓
Tailscale / WireGuard
    ↓
GPU PC
    ↓
vLLM
```

Do not expose:

```text
vLLM :8000
Ollama :11434
```

directly to the Internet.

Only the backend should access the inference server.

---

# Phase 24 — Recommended Component Architecture

```text
                 User
                   │
                   ▼
            ASP.NET Backend
                   │
                   ▼
          Question Classifier
              CPU Model
                   │
                   ▼
          Classification Rules
                   │
                   ▼
            Cache Key Builder
                   │
                   ▼
                Redis
             ↙         ↘
           HIT         MISS
            │             │
            │             ▼
            │      PostgreSQL Library
            │         ↙        ↘
            │       HIT        MISS
            │        │           │
            │        │           ▼
            │        │       LLM Queue
            │        │           │
            │        │           ▼
            │        │      Private VPN
            │        │           │
            │        │           ▼
            │        │      GPU PC / vLLM
            │        │           │
            │        │           ▼
            │        │       LLM Result
            │        │           │
            │        │           ▼
            │        └──── Save DB + Redis
            │                    │
            └──────────────┬─────┘
                           ▼
                       Response
```

---

# MVP Scope

Implement first:

- [x] Define 20–30 question intents
- [x] Create classification DTO
- [x] Implement simple classifier
- [x] Return confidence score
- [x] Return personalization level
- [x] Add confidence threshold configuration
- [x] Implement deterministic cache-key builder
- [x] Include spread, card, position, orientation, locale
- [x] Include prompt version
- [x] Include interpretation version
- [x] Add Redis
- [x] Cache final structured reading result
- [x] Return cached response on HIT
- [x] Generate by reading mode on MISS
- [x] Save valid Standard or Deep result to Redis
- [x] Add cache HIT/MISS logging
- [x] Add LLM call metrics
- [x] Skip shared cache for HIGH personalization
- [x] Skip shared cache for low-confidence classification
- [x] Protect LLM calls with concurrency limit
- [x] Add user-selectable STANDARD and DEEP reading modes
- [x] Keep STANDARD generation rule-based with zero LLM calls
- [x] Require backend premium entitlement for DEEP in production
- [x] Add development-only entitlement bypass
- [x] Integrate private Ollama `qwen3:8b` for DEEP generation
- [x] Include reading mode in every finished-answer cache key
- [x] Include model version in DEEP cache identity
- [x] Validate structured output, card identity/order, and Thai output language
- [x] Cache valid DEEP output and avoid Ollama on HIT

---

## Python gRPC Classifier Upgrade

- [x] Define versioned classifier protobuf contract
- [x] Add Python gRPC CPU classifier service
- [x] Add TF-IDF + Logistic Regression seed model
- [x] Return classifier source and model version
- [x] Add asynchronous ASP.NET gRPC client
- [x] Keep predefined topics local to C#
- [x] Validate Python taxonomy output in C#
- [x] Keep C# personalization safety guard
- [x] Add deadline and conservative fallback
- [x] Add Python service tests, C# orchestration tests, and an end-to-end gRPC smoke test
- [x] Add classifier health check and Docker service

---

# MVP Phase 2

- [x] Persist generated answers in PostgreSQL
- [x] Redis MISS → check PostgreSQL
- [x] Repopulate Redis from PostgreSQL
- [x] Add hit_count
- [x] Add cache analytics dashboard
- [x] Add distributed lock to prevent cache stampede
- [x] Generate multiple answer variants per cache key
- [x] Pre-generate 1-card readings
- [x] Store classifier training examples
- [x] Add human review workflow for classifier labels

---

# Future

- [x] Train custom classifier using real user questions
- [x] Support semantic similarity cache
- [x] Add embedding model
- [x] Detect similar question intent automatically
- [x] Cache base interpretation separately from final prose
- [x] Add additional premium model tiers beyond `qwen3:8b`
- [x] Add offline cache warming
- [x] Add multi-GPU workers
- [x] Add GPU failover
- [x] Add cloud-GPU fallback
- [x] Add automated prompt A/B testing
- [x] Add response-quality scoring

---

# MVP Phase 3 — Infrastructure

- [x] Run the complete platform through Docker Compose

Acceptance scope:

- Containerize the ASP.NET API and Next.js web application.
- Include the web, API, classifier, PostgreSQL, Redis, and local inference services in `docker-compose.yml`.
- Add a one-shot database migration service that completes before the API becomes ready.
- Add health checks and health-based startup dependencies for every required service.
- Use internal networks for PostgreSQL, Redis, classifier, and inference traffic; publish only user-facing development ports.
- Persist PostgreSQL, Redis, classifier artifacts, and local model data in named volumes.
- Move credentials and deployment-specific endpoints to environment variables or Docker secrets, with a checked-in example environment file containing no production secrets.
- Support local-GPU and external-GPU profiles without exposing the inference endpoint publicly.
- Verify a clean `docker compose up --build` starts the stack and passes web, API health, Swagger/OpenAPI, classifier health, database, Redis, cache, and reading smoke tests.

---

# Important Design Rules

1. Do not use exact raw user question as the main shared-cache key.
2. Classify the question into reusable intent first.
3. Preserve card position and orientation.
4. Include language in the cache key.
5. Include prompt and Tarot interpretation versions.
6. Do not reuse generic finished responses for highly personalized questions.
7. Do not call the GPU when Redis already has an eligible finished answer.
8. Prevent multiple simultaneous cache misses from producing duplicate LLM calls.
9. Store structured JSON, not only plain text.
10. Treat generated cached readings as a reusable content library.

---

# Target Runtime Behavior

Example 1:

```text
Question:
"Should I change my job?"

Classifier:
CAREER / CAREER_CHANGE_JOB / 0.95 / LOW

Cards:
PAST      = TOWER UPRIGHT
PRESENT   = MAGICIAN UPRIGHT
DIRECTION = STAR UPRIGHT

Redis:
HIT

Result:
Return cached answer

LLM GPU:
NOT USED
```

Example 2:

```text
Question:
"Should I change my job?"

Classifier:
CAREER / CAREER_CHANGE_JOB / 0.95 / LOW

Cards:
PAST      = TOWER UPRIGHT
PRESENT   = MAGICIAN REVERSED
DIRECTION = STAR UPRIGHT

Redis:
MISS

Result:
Call LLM
Save finished answer
Return

LLM GPU:
USED ONCE
```

Example 3:

```text
Question:
"I have worked with my brother for 8 years and he wants to sell
our company because his family is moving overseas. What should I do?"

Classifier:
CAREER / CAREER_BUSINESS / 0.91 / HIGH

Result:
Skip shared final-answer cache
Call LLM with personal context

LLM GPU:
USED
```

---

# Success Criteria

The implementation is successful when:

```text
Same intent
+
Same spread
+
Same ordered cards
+
Same orientation
+
Same locale
+
Same prompt version
+
Same interpretation version
```

produces:

```text
CACHE HIT
```

and avoids an LLM call.

While:

```text
Personalized question
Low-confidence classification
Different card orientation
Different card position
Different spread
Different intent
Different language
Different prompt version
```

produces either:

```text
CACHE MISS
```

or:

```text
SKIP SHARED CACHE
```

as appropriate.

---

# Final Target Architecture

```text
Question
   ↓
Small CPU Classifier
   ↓
Domain + Intent + Confidence + Personalization
   ↓
Shared-cache eligible?
   │
   ├── NO
   │    ↓
   │   LLM
   │
   └── YES
        ↓
   Cache Key Builder
        ↓
      Redis
    ↙       ↘
  HIT       MISS
   ↓          ↓
Return      PostgreSQL
              ↓
         HIT / MISS
              ↓
          LLM if needed
              ↓
      Save persistent answer
              ↓
          Save Redis
              ↓
            Return
```

Primary optimization goal:

```text
MAXIMIZE:

Reusable finished-answer cache hits

WHILE MAINTAINING:

Question relevance
Reading correctness
Safe personalization boundaries
```

---

# Next Task — Frontend Reading Journey Animation and Visual Polish

Status: **Implemented — browser QA and screenshots pending**

## Goal

Improve the main reading page so the transition from question → shuffle → selection → generation → completed reading feels intentional, beautiful, and responsive instead of switching abruptly between static UI states.

This is a frontend-only experience task. It must use the current API contracts and must not change the final DEEP behavior documented in `SUMMARY.md`.

Thai product intent:

```text
เพิ่ม animation ตอนสับไพ่
แสดง loading ที่สวยและเข้าใจง่ายระหว่างรอ API สร้างคำทำนาย
เมื่อได้ response ให้เปิดไพ่และแสดงคำทำนายอย่างนุ่มนวล
```

## Current UI Problem

The current page uses one boolean `busy` state for several different operations:

```text
shuffle request
resolve selected cards
generate reading
```

While `busy` is true, the UI only disables controls and shows a small `working` status. Users cannot tell whether the deck is shuffling, cards are being revealed, or the reading is being generated. When the response arrives, the entire deck disappears and the complete reading appears immediately without a visual transition.

The affected implementation is primarily:

```text
apps/web/app/page.tsx
apps/web/app/globals.css
apps/web/public/images/tarot-card-back.webp
```

## Required Interaction State Model

Replace the single visual meaning of `busy` with an explicit reading-journey phase. A suitable model is:

```ts
type ReadingPhase =
  | "IDLE"
  | "SHUFFLING"
  | "SELECTING"
  | "RESOLVING"
  | "GENERATING"
  | "REVEALING"
  | "COMPLETE"
  | "ERROR";
```

The exact implementation may use a reducer or equivalent state machine, but impossible state combinations should be avoided. Network state, animation state, disabled controls, status copy, and visible content must agree with the current phase.

Expected phase flow:

```text
IDLE
  → SHUFFLING
  → SELECTING
  → RESOLVING
  → GENERATING
  → REVEALING
  → COMPLETE
```

On failure:

```text
SHUFFLING / RESOLVING / GENERATING
  → ERROR
  → allow a safe retry without losing useful user input
```

## 1. Shuffle Animation

When the user presses Shuffle:

- Disable conflicting controls immediately.
- Animate the visible deck into a temporary stacked/shuffling composition.
- Use a short combination of card translation, rotation, and stagger to suggest a real shuffle.
- Redeal the card backs into the selectable grid after the shuffle API succeeds.
- Keep the motion subtle and premium; avoid a playful casino or slot-machine style.
- Use the existing Tarot card-back artwork and the existing gold, teal, rose, and dark visual palette.
- Target approximately 600–1,000 ms for the main shuffle sequence.
- Coordinate the animation and network request without delaying a slow request or flashing through a fast request. A fast response may wait for the minimum meaningful animation; a slow response remains in the shuffling state until both are ready.
- Do not shuffle or randomize cards in the browser. The backend remains authoritative for the real deck/session.

Suggested motion sequence:

```text
grid cards dim
  → representative cards gather toward center
  → cards cross/rotate in two or three short passes
  → stack settles
  → cards deal back into grid with a small stagger
```

## 2. Card Selection Feedback

Improve the existing selected-card lift so selection feels deliberate:

- Selected cards rise slightly, receive a gold focus ring, and show a subtle glow.
- Selection order should be visible for multi-card spreads, for example `1`, `2`, `3`, rather than only showing the deck index.
- Deselecting a card should animate it back to the deck cleanly.
- The Reveal button should become visually ready only when the required number of cards is selected.
- Keyboard selection, focus visibility, `aria-pressed`, and disabled states must continue working.

## 3. Waiting for Resolve and Generate

After the user presses Reveal Reading:

- Do not leave the full 78-card grid as the main loading visual.
- Transition the selected card backs into their spread positions.
- Keep those selected cards visible while `/api/readings/generate` is running.
- Show a calm, animated reading indicator using subtle glow, breathing, shimmer, or orbiting symbols.
- Use real phase labels rather than a fake percentage.
- Do not imply the reading is complete before the API response arrives.
- STANDARD may complete quickly; avoid a loading-state flash shorter than approximately 250–350 ms.
- DEEP may take much longer; its loading treatment must remain visually stable for an extended request and must not continuously create expensive DOM nodes or timers.

Suggested localized status copy:

```text
EN
SHUFFLING  = Shuffling the deck…
RESOLVING  = Revealing your selected cards…
GENERATING = Reading the pattern in your cards…
DEEP       = Connecting your question with the full spread…

TH
SHUFFLING  = กำลังสับไพ่…
RESOLVING  = กำลังเปิดไพ่ที่คุณเลือก…
GENERATING = กำลังอ่านความสัมพันธ์ของไพ่…
DEEP       = กำลังเชื่อมโยงคำถามของคุณกับไพ่ทั้งชุด…
```

Accessibility requirements:

- Mark the reading region with `aria-busy="true"` during network work.
- Announce meaningful phase changes through one `role="status"` or `aria-live="polite"` region.
- Do not repeatedly announce decorative loading messages.
- Preserve a readable text status even if animation or CSS fails.

## 4. Completed Response Reveal

When the generation response arrives:

- Move from `GENERATING` to `REVEALING`; do not replace the entire layout in one frame.
- Flip or crossfade selected card backs into the returned card faces in spread order.
- Stagger card reveals by approximately 120–200 ms.
- Respect reversed orientation visually without making reversed cards look automatically negative.
- Reveal the title and summary after the cards begin opening.
- Fade/slide the main theme, insights, reflection question, and closing message in a restrained sequence.
- Move focus to the completed reading heading or announce completion so keyboard and screen-reader users know the result is ready.
- Scroll the result into view only when necessary and use non-jarring behavior.
- Set the final phase to `COMPLETE` only after the response is stored in state and the reveal transition has started safely.

The reveal must not delay access to content for several seconds. The complete response should remain selectable, readable, and present in the DOM without requiring animation to finish.

## 5. Visual Direction

Aim for:

```text
mystical
calm
premium
cinematic but restrained
```

Avoid:

```text
casino effects
large confetti explosions
constant bouncing
rapid flashing
fake progress percentages
animations that compete with reading text
```

Implementation preferences:

- Start with React state plus CSS keyframes/transitions; do not add an animation dependency unless CSS is demonstrably insufficient.
- Prefer compositor-friendly `transform` and `opacity` animations.
- Avoid animating large layout properties repeatedly.
- Keep card movement stable on desktop and mobile.
- Use CSS custom properties for duration, stagger, glow color, and easing so the motion system remains consistent.
- Extract small presentational components such as `ShuffleStage`, `ReadingLoader`, or `CardReveal` if `page.tsx` becomes difficult to maintain.
- Before implementation, follow `apps/web/AGENTS.md` and read the relevant installed Next.js 16.3.2 documentation under `apps/web/node_modules/next/dist/docs/`.

## 6. Reduced Motion and Performance

Support `prefers-reduced-motion: reduce`:

- Replace shuffle movement with a short opacity transition or immediate state change.
- Remove looping float, orbit, shimmer, and 3D flip animation.
- Show all response content immediately when it arrives.
- Preserve status text and functional state changes.

Performance acceptance:

- No permanent `requestAnimationFrame` loop.
- Clean up timeouts and animation listeners on reset or unmount.
- No growing particle collection during a long DEEP request.
- Avoid layout shift when moving between loading and completed states.
- Test at narrow mobile width and common desktop width.

## 7. Error and Retry Behavior

- A shuffle error returns to a usable pre-shuffle state and preserves the question/settings.
- A resolve error preserves the selected indexes when safe so the user can retry.
- A generation error keeps the resolved/selected cards visible and offers a clear retry action.
- Error copy remains localized in English and Thai.
- Controls must never remain permanently disabled after rejection, timeout, abort, or unexpected parsing failure.
- Starting a new shuffle, changing locale/spread/question/mode, or retrying must cancel or invalidate stale animation completion callbacks.
- A late response from an obsolete request must not overwrite the current reading flow.

## 8. Localization

Add English and Thai copy for:

- Shuffle progress
- Card resolving progress
- Standard reading generation
- Deep reading generation
- Reading ready/completed announcement
- Retry generation action

Do not hard-code user-visible status text directly in JSX. Add it to the existing localized `copy` structure.

## 9. Testing and Verification

Required automated checks:

- [x] TypeScript typecheck passes with `npm run typecheck` in `apps/web`.
- [x] Production build passes with `npm run build` in `apps/web`.
- [x] State-transition tests are not required because the frontend has no test framework installed.
- [x] Reduced-motion behavior does not depend on animation completion events.
- [x] No stale response can replace a newer/reset flow.

Required browser QA:

- [ ] Shuffle animation completes and the deck becomes selectable.
- [ ] Fast STANDARD response does not flash an unreadable loader.
- [ ] Slow DEEP response remains stable and clearly communicates work.
- [ ] Selected cards remain visible while waiting.
- [ ] Completed cards reveal in spread order.
- [ ] Reversed cards render intentionally.
- [ ] Error and retry paths recover without refreshing the page.
- [ ] Keyboard-only use remains possible.
- [ ] Screen-reader status announcements are meaningful and not repetitive.
- [ ] `prefers-reduced-motion` removes non-essential motion.
- [ ] Layout works at approximately 360 px mobile width and 1440 px desktop width.
- [ ] English and Thai copy both fit without overlap or clipping.

## Acceptance Criteria

- [x] Pressing Shuffle produces a visible, polished shuffle/deal transition tied safely to the API request.
- [x] The UI shows distinct `SHUFFLING`, `RESOLVING`, `GENERATING`, `REVEALING`, and `COMPLETE` states.
- [x] Waiting for `/api/readings/generate` has an accessible, localized, visually stable loading experience.
- [x] The selected cards—not the full deck—remain the visual focus while waiting.
- [x] A successful response reveals cards and reading sections smoothly instead of replacing the page abruptly.
- [x] Failure keeps the flow recoverable and offers retry without losing the user's question/settings.
- [x] Motion respects reduced-motion preferences and remains performant during long DEEP requests.
- [x] Existing API request bodies and response handling remain compatible.
- [x] No backend behavior, DEEP prompt content, classifier boundary, or cache policy is changed by this task.

## Definition of Done

- [x] Implementation completed in the frontend.
- [x] English and Thai UI copy completed.
- [x] Typecheck and production build pass.
- [ ] Browser QA completed for desktop, mobile, keyboard, errors, STANDARD, and DEEP.
- [ ] Screenshots or a short recording document the shuffle, waiting, and completed states.
- [x] Relevant frontend behavior is summarized in `SUMMARY.md` after implementation.
