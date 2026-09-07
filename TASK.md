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

---

# Next Task — Classifier Dataset Expansion and Confidence-Gated Reading Cache

Status: **Implemented behind disabled gates; human review and rollout approval pending**

## Goal

Increase classifier coverage and precision before expanding shared finished-answer caching.

After the classifier quality gate passes, add a cache-only wrapper that can reuse a reading when:

- Classification confidence is strictly greater than `0.90`.
- The classified domain and intent match.
- Reading mode, spread, ordered card positions, card IDs, orientations, locale, and content versions match.
- The request and generated response are safe for shared reuse.

Different raw questions may reuse the same answer when all cache identity and safety conditions match. The raw question and user ID must not be part of the shared cache key.

## Non-Negotiable DEEP Boundary

This task must not change the existing `DEEP` generation pipeline on a cache miss.

Preserve:

- Backend premium-entitlement verification.
- The exact raw question sent to the LLM.
- Existing system and user prompt content.
- Model-tier selection and inference routing.
- Timeout, retry, failover, and concurrency behavior.
- Structured-output validation and quality scoring.
- The public request and response contracts.

Classification is used only for cache eligibility and cache identity. Never add classifier domain, intent, confidence, personalization, card meanings, or rule-engine content to the `DEEP` LLM prompt.

Required `DEEP` miss flow:

```text
Cache MISS or cache ineligible
    → run the existing DEEP flow unchanged
    → validate the LLM response
    → evaluate whether the result is safe for shared reuse
    → save only when eligible and safe
    → return the original validated response
```

A cache infrastructure failure must fall through to the existing `DEEP` flow. Cache availability must not become a requirement for generating a reading.

Until this task is implemented, evaluated, and explicitly enabled, the authoritative current `DEEP` behavior in `SUMMARY.md` remains unchanged.

## Delivery Order

Implement this task in two gated stages:

```text
Stage A — Dataset expansion, training, calibration, and evaluation
    ↓ quality gate passes
Stage B — Confidence-gated cache wrapper and monitored rollout
```

Do not enable Stage B merely because the model trains successfully. It must meet the evaluation and safety criteria below.

---

## Stage A — Expand the Classification Dataset

### 1. Keep the Taxonomy Fixed

Use the existing domain and intent taxonomy unless an explicit taxonomy migration is approved.

Every dataset label must be validated against the C# and Python shared taxonomy. Unknown labels must fail dataset validation rather than silently becoming new production intents.

Always retain:

```text
PERSONAL_CUSTOM
```

The classifier must be allowed to reject uncertain, mixed-domain, out-of-domain, and highly personalized questions. The goal is not to force every possible question into a reusable intent.

### 2. Dataset Coverage

Expand both English and Thai training data with:

- Natural short and long questions.
- Formal and informal wording.
- Common spelling mistakes and incomplete grammar.
- Thai colloquial wording.
- Mixed Thai/English questions.
- Multiple paraphrase families for every intent.
- Questions that omit obvious domain keywords.
- Questions containing misleading keywords.
- Near-boundary examples between easily confused intents.
- Multi-domain questions.
- Out-of-domain questions.
- Highly personalized questions.
- Questions with names, employers, locations, dates, timelines, and financial amounts.
- Adversarial examples that should become `PERSONAL_CUSTOM`.

Initial dataset targets:

```text
Important intents:
    300–500 reviewed examples per intent per locale

Lower-volume intents:
    100–200 reviewed examples per intent per locale

PERSONAL_CUSTOM / uncertain / multi-domain / HIGH personalization:
    at least 1,000 reviewed examples across supported locales
```

Synthetic examples may bootstrap coverage, but synthetic labels must not be treated as reviewed production truth. Track their source separately.

### 3. Dataset Record Contract

Each training example should contain at least:

```text
question
locale
domain
intent
personalization
source
reviewStatus
paraphraseGroup
createdAt
reviewedAt
```

Recommended source values:

```text
SEED
SYNTHETIC
PRODUCTION_REVIEWED
MANUAL_REVIEWED
HARD_NEGATIVE
```

Do not store user identity, authentication data, or unrelated personal information. Remove or replace unnecessary names, contact details, and sensitive identifiers before a production question becomes training data.

### 4. Review Workflow

- Require human review before production examples enter the trusted training split.
- Record both the original predicted label and reviewed label.
- Support correcting domain, intent, and personalization independently.
- Record reviewer time and model version for auditability.
- Deduplicate exact and normalized questions.
- Flag near-duplicates and paraphrases for grouped splitting.

### 5. Train, Validation, and Test Splits

Prevent evaluation leakage:

- Split by `paraphraseGroup`, not by individual row.
- Keep near-duplicates in the same split.
- Keep a final test set that is never used for model or threshold tuning.
- Stratify by intent, locale, personalization, and source where possible.
- Include English, Thai, mixed-language, typo, boundary, and rejection subsets in the final test report.

Recommended split:

```text
Training     70%
Validation   15%
Final test   15%
```

### 6. Model Training and Calibration

Continue using the CPU classifier architecture unless evaluation proves it insufficient:

```text
TF-IDF
    +
Logistic Regression
```

Required training behavior:

- Use deterministic random seeds.
- Address class imbalance explicitly.
- Tune only against the training and validation splits.
- Calibrate returned probabilities using validation data.
- Never calibrate against the final test set.
- Export taxonomy version, training-data version, model version, metrics, and build timestamp with the artifact.
- Keep the previous model artifact available for rollback.

Confidence must represent observed correctness, not merely the classifier's uncalibrated maximum probability.

### 7. Confidence Policy

The new shared-cache threshold is:

```text
confidence > 0.90
```

The comparison is strict. A confidence value equal to `0.90` is not eligible.

Make the threshold configurable:

```json
{
  "Classifier": {
    "MinimumSharedCacheConfidence": 0.90
  }
}
```

Recommended shared-cache safety policy:

```text
confidence > 0.90
AND intent != PERSONAL_CUSTOM
AND personalization != HIGH
```

`HIGH` personalization remains ineligible because the raw `DEEP` question may contain unique information that must not be shared with another user. This guard affects only shared-cache eligibility; it must not alter the generated response.

### 8. Classifier Quality Gate

Stage A passes only when the untouched final test set demonstrates:

- At least `97%` precision among predictions accepted above `0.90` overall.
- At least `95%` accepted precision for every intent enabled for shared caching.
- Explicit per-locale precision and coverage reporting.
- No severe confusion pair is hidden by aggregate accuracy.
- `PERSONAL_CUSTOM` and `HIGH`-personalization rejection behavior is measured separately.
- Confidence calibration error is reported.
- A reviewed error analysis exists for false high-confidence predictions.

If an intent misses the per-intent precision requirement, disable that intent from shared caching even if the global score passes.

Coverage is important, but it must not be improved by lowering precision. Uncertain questions should continue to generate without shared-cache reuse.

### 9. Classifier Metrics

Track at least:

```text
classifier_requests_total
classifier_accepted_total
classifier_rejected_total
classifier_fallback_total
classifier_invalid_response_total
classifier_confidence_distribution
classifier_intent_distribution
classifier_latency
accepted_precision_by_intent
accepted_precision_by_locale
accepted_coverage_by_intent
personal_custom_rejection_rate
high_personalization_rejection_rate
```

Do not log raw questions in ordinary application metrics or logs.

---

## Stage B — Confidence-Gated Shared Cache Wrapper

Stage B may begin only after Stage A passes and the approved model artifact is deployed.

### 10. Cache Eligibility Flow

For both reading modes, classification is a cache preflight only:

```text
Classify raw question
    ↓
confidence > 0.90?
intent reusable?
personalization safe?
    ↓ YES
Build shared cache key
    ↓
Redis lookup
    ↓ MISS
PostgreSQL lookup
    ↓ MISS
Generate through the existing mode-specific flow
    ↓
Validate response
    ↓
Shared-content safety check
    ↓ SAFE
Persist PostgreSQL + Redis
```

Ineligible requests skip the shared cache and continue through their existing mode-specific generation flow.

### 11. Cache Key

The shared cache key must include:

```text
cache schema version
taxonomy version
domain
intent
reading mode
spread
locale
ordered position:cardId:orientation values
prompt version
interpretation version
```

For `DEEP`, also include:

```text
model tier
generation model version
```

Do not include:

```text
raw question
user ID
session ID
authentication claims
```

Card order must remain the request/spread order. Do not alphabetically sort cards.

### 12. Shared DEEP Content Safety

Confidence above `0.90` is necessary but is not sufficient to share a `DEEP` response.

The current `DEEP` LLM receives the exact raw question and may reflect question-specific information in its answer. Before storing a `DEEP` result in the shared cache, reject shared persistence when the question or response contains non-reusable details such as:

- Personal names or identifiable people.
- Employer, company, school, or location names.
- Exact dates, ages, durations, or timelines.
- Specific financial amounts, account details, or unique purchases.
- Unique relationship or family history.
- Multiple combined life domains.
- Direct quotation or close repetition of unique question text.
- Other details that could expose one user's context to another user.

This check must only decide whether to store the response. It must not rewrite, generalize, or change the response returned to the requesting user.

If safety is uncertain:

```text
Return the generated response to the current user
Do not save it to the shared cache
```

### 13. DEEP Cache Behavior

Cache hit:

```text
Return the cached validated structured response
Do not call the LLM
Report CacheStatus.HIT
```

Cache miss:

```text
Run the current DEEP generation flow unchanged
Wait for the complete LLM response
Validate it using the existing validator
Store only if classification and content are safe
Return CacheStatus.MISS when stored
Return CacheStatus.SKIPPED when not shareable
```

Classifier failure, timeout, invalid taxonomy output, or confidence `<= 0.90`:

```text
Run the current DEEP flow unchanged
Do not read or write the shared cache
Return CacheStatus.SKIPPED
```

### 14. Persistence and Stampede Protection

- Reuse Redis for the fast runtime lookup.
- Reuse PostgreSQL for persistent generated-answer storage.
- Reuse the existing distributed lock per cache hash.
- After acquiring the lock, check Redis and PostgreSQL again before generating.
- Do not hold a lock for an ineligible request.
- Set lock expiry above the maximum configured `DEEP` generation timeout.
- Continue generating if Redis, PostgreSQL, or the lock service is unavailable.
- Increment persistent and runtime hit counters only after a successful cache reuse.

### 15. Cache Metrics

Track separately by reading mode:

```text
cache_preflight_requests
cache_preflight_eligible
cache_preflight_rejected_confidence
cache_preflight_rejected_personalization
cache_preflight_rejected_intent
cache_preflight_rejected_content_safety
cache_hits
cache_misses
cache_store_success
cache_store_failure
cache_lookup_latency
cache_hit_rate_among_eligible
llm_requests_avoided
```

Classifier accepted coverage and cache hit rate are different metrics. A larger dataset may increase accepted coverage, while actual cache hit rate still depends on repeated intents, spreads, and card selections.

### 16. Rollout

Add a server-side feature flag for the new `DEEP` cache wrapper:

```json
{
  "DeepSharedCache": {
    "Enabled": false
  }
}
```

Recommended rollout:

```text
1. Shadow classification only; no DEEP cache reads or writes.
2. Measure confidence, coverage, latency, and safety rejection.
3. Enable writes without serving hits to build and inspect a candidate library.
4. Human-review a sample of candidate shared responses.
5. Enable cache reads for approved intents only.
6. Expand gradually while monitoring wrong-reuse reports.
```

Disabling the flag must immediately restore the authoritative current `DEEP` behavior without requiring a deployment or data deletion.

### 17. Startup Cache Warmup

Warm approved cache entries automatically after the API server starts.

Reuse and extend the existing `CacheWarmupService` and warmup job model. Do not create a second unrelated warmup pipeline.

Startup behavior:

```text
API starts and becomes ready
    → wait for a configurable startup delay
    → acquire one distributed startup-warmup lock
    → rehydrate current-version Redis entries from PostgreSQL
    → enqueue approved missing warmup combinations
    → generate with bounded background concurrency
    → validate and persist through the normal cache services
```

The API must become ready before warmup work begins. Startup warmup must never block HTTP readiness, Swagger availability in Development, or normal reading requests.

Recommended configuration:

```json
{
  "StartupCacheWarmup": {
    "Enabled": false,
    "DelaySeconds": 15,
    "RehydrateRedisFromPostgres": true,
    "Locales": ["en", "th"],
    "ReadingModes": ["STANDARD"],
    "ApprovedIntents": [],
    "Spreads": ["DAILY_1"],
    "Variants": 1,
    "MaxCombinationsPerStartup": 500,
    "MaxConcurrency": 1,
    "MaxDeepGenerationsPerStartup": 0,
    "RetryCount": 2,
    "RetryDelaySeconds": 10
  }
}
```

Requirements:

- Keep startup warmup disabled by default until Stage A and the rollout gates pass.
- Begin only after application startup and the configured delay.
- Cancel promptly during application shutdown.
- Use a Redis distributed lock so multiple API replicas do not run the same startup warmup simultaneously.
- Make the process idempotent: check Redis and PostgreSQL before generating each combination.
- Prefer PostgreSQL-to-Redis rehydration over regeneration when a current-version persistent answer already exists.
- Use the same cache-key builder, validators, safety rules, persistence, TTL, and version policy as live traffic.
- Process only approved intents, locales, spreads, modes, model tiers, and content versions.
- Apply a hard per-start combination limit and bounded concurrency.
- Yield capacity to live reading traffic; startup warmup must use the existing inference concurrency controls and must not bypass queue limits.
- Record a warmup job that is visible through the existing admin warmup status endpoints.
- A Redis, PostgreSQL, classifier, or LLM failure must be logged and counted without stopping the API.
- Restarting the server must skip entries that are already warm and current.

`STANDARD` warmup may generate approved deterministic rule-engine combinations.

`DEEP` startup generation is disabled by default. It may be enabled only when all of the following are true:

- Stage A passed.
- `DeepSharedCache.Enabled` is true for writes.
- The intent is approved for shared reads/writes.
- A reviewed, `LOW`-personalization canonical warmup question exists for that locale and intent.
- The generated response passes the same shared-content safety check as live traffic.
- `MaxDeepGenerationsPerStartup` is greater than zero.

The canonical warmup question is input for an offline cache seed only. It must not change the live `DEEP` prompt contract. Never synthesize arbitrary personal questions during startup.

Warmup priority should be based on observed production demand:

```text
1. Current-version PostgreSQL answers missing from Redis
2. Highest-hit approved DAILY_1 combinations
3. Highest-hit approved three-card combinations
4. Explicit admin-configured combinations
```

Do not attempt to pre-generate every three-card combination.

Startup warmup metrics:

```text
startup_warmup_runs
startup_warmup_lock_acquired
startup_warmup_lock_contended
startup_warmup_entries_examined
startup_warmup_redis_rehydrated
startup_warmup_entries_already_present
startup_warmup_entries_generated
startup_warmup_entries_rejected_safety
startup_warmup_failures
startup_warmup_duration
startup_warmup_cancelled
```

---

## Required Tests

### Dataset and Classifier

- [x] Dataset schema validation rejects missing or invalid labels.
- [x] Exact and normalized duplicates are detected.
- [x] Paraphrase groups cannot cross train/validation/test boundaries.
- [x] English, Thai, mixed-language, typo, boundary, and rejection subsets are evaluated.
- [x] Confidence calibration is tested against held-out validation data.
- [x] The final test report includes accepted precision and coverage per intent and locale.
- [x] Model artifacts include reproducible version metadata.

### Cache Eligibility

- [x] Confidence `0.9000` skips the shared cache.
- [x] Confidence greater than `0.90` can become eligible.
- [x] `PERSONAL_CUSTOM` skips the shared cache.
- [x] `HIGH` personalization skips the shared cache.
- [x] Classifier timeout, unavailable service, or invalid response skips the cache safely.
- [x] Raw question and user ID do not affect or appear in the shared cache key.

### Cache Identity

- [x] Different wording with the same accepted intent and identical reading identity produces the same key.
- [x] Different domain or intent produces a different key.
- [x] Different reading mode produces a different key.
- [x] Different spread produces a different key.
- [x] Different locale produces a different key.
- [x] Different card position, card ID, orientation, or order produces a different key.
- [x] Different prompt, interpretation, taxonomy, model-tier, or generation-model version produces a different key where applicable.

### DEEP Preservation

- [x] Ineligible `DEEP` requests execute the existing flow unchanged.
- [x] Eligible cache misses send the exact raw question through the existing prompt path.
- [x] Classification output never enters the LLM prompt.
- [x] A `DEEP` cache hit avoids the LLM.
- [x] A safe validated `DEEP` miss is persisted and can be reused.
- [x] A personalized or content-unsafe response is returned but not stored.
- [x] Entitlement is verified before returning either a generated or cached `DEEP` response.
- [x] Cache infrastructure failure falls through to the existing `DEEP` generation path.
- [x] Concurrent identical eligible misses generate at most one shared answer.

### Startup Warmup

- [x] Startup warmup begins only after the API is ready and the configured delay expires.
- [x] Startup warmup does not delay readiness or block normal HTTP requests.
- [x] Only one API replica acquires the distributed startup-warmup lock.
- [x] Current PostgreSQL entries rehydrate Redis without regeneration.
- [x] Existing current-version cache entries are skipped idempotently.
- [x] Warmup respects approved intents, locales, spreads, modes, versions, and hard limits.
- [x] Warmup concurrency is bounded and uses the normal inference limiter.
- [x] Shutdown cancels queued and active warmup work promptly.
- [x] Infrastructure failure does not make the API unhealthy or unavailable.
- [x] `DEEP` warmup remains disabled when `MaxDeepGenerationsPerStartup` is zero.
- [x] `DEEP` warmup uses reviewed canonical questions and stores only content-safe responses.
- [x] Startup-created jobs appear through the existing admin warmup status endpoints.

### API and Documentation

- [x] Existing request and response contracts remain compatible.
- [x] Swagger UI and generated OpenAPI JSON remain aligned if HTTP contracts change.
- [x] Cache status and error envelopes remain explicit shared response types.
- [x] `SUMMARY.md` is updated only when rollout changes the authoritative runtime behavior.

---

## Acceptance Criteria

- [ ] Dataset targets are met or documented per intent with an approved exception.
- [ ] The untouched final test set passes the overall and per-intent precision gates.
- [x] Confidence values are calibrated and the strict `> 0.90` policy is tested.
- [x] Low-confidence, unknown, multi-domain, and highly personalized questions safely skip shared reuse.
- [x] The cache key ignores raw wording while preserving every reading identity input.
- [x] `DEEP` cache misses execute the current generation flow without prompt or routing changes.
- [x] Shared `DEEP` persistence cannot expose unique question or response details to another user.
- [x] Redis/PostgreSQL failures never prevent an otherwise valid reading.
- [x] Cache stampede protection prevents duplicate eligible generation.
- [x] Metrics distinguish classifier coverage, cache eligibility, cache hits, and LLM avoidance.
- [x] Server startup automatically rehydrates and warms only approved cache entries without delaying readiness.
- [x] Startup warmup is idempotent, distributed-lock protected, bounded, observable, and safe to cancel.
- [x] The feature can be disabled immediately to restore current authoritative behavior.

## Definition of Done

- [ ] Bilingual reviewed dataset expanded and versioned.
- [x] Leakage-safe train, validation, and final test splits created.
- [ ] CPU classifier retrained and probability-calibrated.
- [ ] Quality report and reviewed false-positive analysis completed.
- [ ] Stage A quality gate passed.
- [x] Cache-only wrapper implemented behind a disabled-by-default feature flag.
- [x] Cache eligibility, identity, persistence, safety, and concurrency tests pass.
- [x] Startup cache rehydration and warmup service implemented behind disabled-by-default configuration.
- [x] Multi-replica locking, idempotency, limits, cancellation, failure, and warmup-status tests pass.
- [x] Existing API tests pass.
- [x] Swagger/OpenAPI verified if affected.
- [ ] Shadow and write-only rollout observations reviewed.
- [ ] Approved intents enabled gradually for cache reads.
- [ ] `SUMMARY.md` updated after the authoritative runtime policy changes.

---

# AdSense Approval-Ready Monetization

## Completed Implementation

- [x] Add `/th` and `/en` reading routes with matching document language and permanently redirect `/` to `/th`.
- [x] Separate public and admin root layouts, keep `/admin` noindex and Google-script-free, and hide the public Admin link in Production.
- [x] Add localized public navigation, footer, language switching, consent settings, and About, Contact, Privacy, Terms, Cookie Policy, and Disclaimer page scaffolds.
- [x] State that the service is for adults 18+ and for entertainment/self-reflection rather than medical, legal, financial, or other professional advice.
- [x] Add a typed local MDX guide system with draft control, paired translations, authorship, publication/update metadata, canonical URLs, and hreflang metadata.
- [x] Add all eight Thai/English guide pairs as private drafts pending owner review.
- [x] Add useful crawlable explanatory content and responsible guide links below the reading tool while keeping reading routes ad-free.
- [x] Add canonical and Open Graph metadata, article structured data, `/sitemap.xml`, `/robots.txt`, and `/ads.txt`, excluding drafts and `/admin` where required.
- [x] Add fail-closed server runtime validation for site, AdSense, GA4, rollout, and trusted Cloudflare-country configuration.
- [x] Expose a valid AdSense verification meta tag and exact seller line independently of the ad-serving rollout flag.
- [x] Add one labelled, responsive, non-personalized manual ad placement for eligible published guide pages, with reserved space and no empty container on invalid configuration.
- [x] Restrict GA4 to sanitized public guide pageviews, locale, and route without reading questions, answers, topics, cards, identities, authentication data, or admin activity.
- [x] Add fail-closed Thailand, EEA/UK/Swiss, and unknown-region consent handling plus a persistent consent-settings control.
- [x] Add structural content validation for the sixteen guide files and a readiness gate requiring exactly eight reviewed, published translation pairs.
- [x] Add browser coverage for redirects, locale metadata, navigation, legal routes, sitemap, robots, ads.txt, admin isolation, regional fail-closed behavior, keyboard entry, and responsive overflow.
- [x] Pass content structural validation, TypeScript typechecking, the production build, fifteen browser checks, and visual review at 360 px and 1440 px.
- [x] Document monetization configuration, deployment, consent, AdSense, legal-review, and safe testing procedures in `apps/web/MONETIZATION.md`.

## Remaining Launch Work

- [ ] Owner supplies, personally reviews, dates, and publishes all sixteen guide articles; the readiness validator must report all eight bilingual pairs ready.
- [ ] Owner and appropriate legal reviewer finalize operator identity, contact details, Privacy, Terms, Cookie Policy, Disclaimer, and Thailand consent wording.
- [ ] Configure the production domain behind Cloudflare, protect direct origin access, validate `CF-IPCountry`, and prevent unsafe consent-varying cache behavior.
- [ ] Supply the real production `SITE_URL`, contact address, AdSense publisher/slot IDs, and GA4 measurement ID without committing environment values.
- [ ] Configure and publish Google's certified three-choice EEA/UK/Swiss CMP message and verify consent-mode behavior in the Google account.
- [ ] Submit the production site to AdSense and wait until Google reports it as Ready while keeping Auto Ads, anchors, and vignettes disabled.
- [ ] Re-run production consent, indexing, navigation, ad-spacing, Core Web Vitals, and invalid-traffic checks without clicking a live ad.
- [ ] Enable analytics and the single guide ad slot only after all content, legal, consent, provider, and deployment gates pass.
- [ ] Review policy status, invalid traffic, countries, engagement, Core Web Vitals, and revenue monthly after launch.

---

# Next Task — Rewarded Ads for One DEEP Reading

Status: **Implemented behind disabled-by-default rollout flags; live rollout remains blocked on provider eligibility and explicit policy/fraud/legal/cost approval**

## Product Requirement

- [x] Let a user voluntarily complete rewarded ads to earn single-use DEEP reading credits. Seed the database with a default requirement of **three completed rewarded ads** and a default reward of **one DEEP credit**.
- [x] Load both the required rewarded-ad completion count and the number of DEEP credits granted from server-authoritative database settings. Do not trust browser values or use client-supplied overrides.
- [x] Do **not** reward, request, encourage, or count ad clicks. Google forbids compensating users for clicks and other artificial interaction with regular ads. Only a provider-confirmed rewarded-ad completion may count. See [AdSense program policies](https://support.google.com/adsense/answer/48182?hl=en) and [ad placement policies](https://support.google.com/adsense/answer/1346295?hl=en).
- [x] Describe the current configured action accurately in Thai and English, for example with the defaults: “Watch 3 optional rewarded ads to unlock one DEEP reading.” Never use “click three ads” or “watch to support us.”
- [x] Make every rewarded ad a separate, explicit opt-in action. The user can close or decline it, and the normal free STANDARD reading remains usable without penalty.
- [x] Keep the reward non-transferable, usable only inside this service, and without cash value, in line with [Google rewarded inventory policy](https://support.google.com/adsense/answer/9121589?hl=en-EN).
- [x] If an ad is unavailable, has no fill, errors, or cannot load because consent was not granted, show a clear message. Do not substitute a normal display ad and do not ask the user to click an ad.

## Reward and Entitlement Rules

- [x] Count only the database-configured number of distinct provider success events equivalent to Google Publisher Tag's `rewardedSlotGranted`. Opening, viewing part of, clicking, closing, or receiving a `rewardedSlotClosed` event does not count.
- [x] Request and display rewarded ads sequentially; never keep more than one rewarded request active.
- [x] Store progress authoritatively on the server. Browser state may display progress but must not grant DEEP access.
- [x] For authenticated users, associate progress and credits with the user account. If anonymous rewards are supported, use a backend-issued opaque reward session; do not treat `localStorage`, cookies, or a client boolean as proof.
- [x] After the configured number of valid completions, atomically convert the progress into the configured number of single-use DEEP credits and reset the completion count.
- [x] Atomically consume the credit when the API authorizes the DEEP request so concurrent requests cannot reuse it.
- [x] Existing paid/premium DEEP entitlement takes priority and must not consume an ad-earned credit.
- [x] Progress and unused ad-earned credits expire after 24 hours. Limit issuance to one completed reward bundle per user or reward session per rolling 24 hours unless a later cost and abuse review approves another limit.
- [x] Reissue a consumed credit only when the server fails before returning a valid reading. User cancellation and client/network abandonment after a valid response do not automatically create another credit.

## Database Configuration

- [x] Add a migrated and seeded singleton rewarded-DEEP settings record with `RequiredAdCompletions=3` and `DeepCreditsPerCompletedBundle=1`.
- [x] Require both settings to be positive, enforce reviewed upper bounds, and fail closed when the record is missing or invalid; do not silently enable unlimited ads or credits.
- [x] Add authenticated admin API and `/admin` controls to read and update both settings, including optimistic concurrency and an audit timestamp. Never expose unrelated configuration or secrets.
- [x] Snapshot the effective settings onto each newly created reward session. A later admin change applies only to new sessions, so progress and the promised reward cannot change while a user is completing a bundle.

## Provider Feasibility Gate

- [x] Keep guide-page display ads on AdSense, but evaluate **Google Ad Manager rewarded web inventory** for this feature because its web API exposes rewarded lifecycle events. See [rewarded ads for web](https://support.google.com/admanager/answer/9116812?hl=en) and the [GPT rewarded-ad sample](https://developers.google.com/publisher-tag/samples/display-rewarded-ad).
- [x] Do not assume AdSense Offerwall can implement the custom three-completion counter. Its documented rewarded-ad choice grants access after a completed ad and is managed by Google; use it only if a one-completion content-access model is accepted. See [AdSense Offerwall rewarded ad](https://support.google.com/adsense/answer/12726063?hl=en).
- [ ] Complete a provider/account eligibility spike before implementation. Confirm that rewarded web inventory is available for this publisher, Thailand traffic, the production domain, and the intended mobile/desktop placements.
- [ ] Record and accept the fraud model before launch: Google Ad Manager does not currently support server-side verification for rewarded ads on the web, so a browser grant event can be forged. Authentication, signed short-lived sessions, idempotency, rate limits, replay protection, and the daily cap reduce risk but do not make the signal cryptographically authoritative.
- [ ] If that residual fraud risk is unacceptable, do not ship the three-ad feature. Use the provider-managed one-ad Offerwall flow or retain paid-only DEEP access instead.
- [x] Keep the rewarded-access feature disabled by default in Development, test, unknown regions, and Production until policy, consent, abuse, and cost reviews pass.

## Consent, Privacy, and Placement

- [x] Load no rewarded-ad script before the visitor has given the advertising consent required by the regional consent policy in the AdSense rollout plan.
- [x] Never send reading questions, generated answers, selected cards/topics, user IDs, authentication tokens, or DEEP entitlement data to the advertising provider.
- [x] Put rewarded controls on a dedicated unlock surface, clearly labelled as advertising and separated from navigation, reading controls, and card interactions to prevent accidental clicks.
- [x] Keep `/admin`, `/th`, `/en`, reading results, and legal pages free of regular display ads. A rewarded ad may appear only after an explicit unlock action; it must never launch automatically.
- [x] Update Privacy, Terms, Cookie Policy, and consent copy to explain the rewarded provider, purpose, data use, expiry, withdrawal effects, reward limits, and lack of cash value before enabling the feature.

## Proposed API and Storage Work

- [x] Add an authenticated or opaque-session reward status endpoint that returns the valid completion count, required completion count, DEEP credits granted per completed bundle, expiry, available DEEP credits, and next eligible time.
- [x] Add a reward-session endpoint that issues a signed, short-lived, single-purpose nonce for one rewarded-ad attempt.
- [x] Add an idempotent grant endpoint that accepts one nonce once, records the provider grant event, applies rate limits, and returns updated progress. It must reject expired, replayed, mismatched, or already-used nonces.
- [x] Extend DEEP authorization to consume either the existing premium entitlement or one valid ad-earned credit without trusting client-supplied entitlement flags.
- [x] Persist only the minimum reward ledger needed for correctness and abuse controls; never persist ad click data or reading content in it.
- [x] Document all new endpoints, authentication, request/response schemas, error envelopes, and rate-limit responses in generated OpenAPI and Swagger UI, following the repository API-documentation requirements.

## Verification and Acceptance Criteria

- [x] With seeded defaults, zero, one, or two valid rewarded grants cannot authorize DEEP; exactly three distinct valid grants issue one credit.
- [x] With valid non-default database settings, the configured completion threshold issues exactly the configured number of credits, and changing settings does not alter an in-progress session's snapshotted threshold or promised reward.
- [x] Ad clicks, partial views, ad-ready events, closes, skips, errors, and no-fill responses never increment progress.
- [x] Replaying a grant, refreshing the page, changing browser state, or sending concurrent requests cannot duplicate progress or credits.
- [x] Each credit authorizes exactly one DEEP reading and is consumed at most once under concurrency.
- [x] Paid premium users keep DEEP access without watching ads or consuming an ad-earned credit.
- [x] Declining advertising consent prevents ad requests and leaves STANDARD readings functional.
- [x] With the feature flag off, current DEEP authorization and all public/admin routes behave exactly as before.
- [x] Browser tests cover dynamic Thai and English threshold/reward wording, keyboard access, opt-in/close behavior, progress recovery, consent withdrawal, no-fill, provider errors, and one active rewarded slot.
- [x] API tests cover seeded database defaults, validated admin updates, settings snapshots, authentication, expiry, daily caps, nonce signing, idempotency, replay protection, rate limiting, atomic multi-credit grant/consume behavior, and failure recovery.
- [x] Production rollout remains blocked until provider eligibility is confirmed, legal/policy copy is reviewed, consent tests pass, and live-ad testing avoids all manual clicks.

# Next Task — User Login and Premium DEEP Access

Status: **Implemented; production SMTP/legal approval and production-shaped migration QA remain rollout gates**

## Product Requirement

- [x] Add a secure user account and login system for the public web application.
- [x] Let an authorized administrator create users and grant, extend, revoke, or inspect premium DEEP access.
- [x] Allow a currently entitled premium user to select and generate DEEP readings without watching rewarded ads or consuming ad-earned credits.
- [x] Show clear Thai and English account, login, logout, premium status, expiry, and access-denied states without exposing internal authorization details.
- [x] Keep STANDARD readings available without a premium account and preserve the existing anonymous experience.

## Authentication and Session Security

- [x] Use ASP.NET Core Identity or an equivalently reviewed database-backed authentication system with unique normalized email addresses and strong adaptive password hashing. Never store or log plaintext passwords.
- [x] Use server-managed `HttpOnly`, `Secure`, appropriately scoped `SameSite` session cookies for the same-origin web/API deployment. Do not store bearer tokens or entitlement claims in `localStorage`.
- [x] Add register, login, logout, current-user, email-verification, and password-reset flows. Keep public registration disabled by default until outbound email and abuse controls are configured; administrators must still be able to create users safely.
- [x] Add CSRF protection to state-changing cookie-authenticated endpoints, rotate the session on login and privilege changes, and invalidate active sessions after password reset, account disablement, or premium revocation.
- [x] Rate-limit login, registration, verification, and password-reset attempts; add lockout/backoff and enumeration-resistant responses.
- [x] Require authenticated role-based authorization for `/admin` and premium-management APIs. Do not expose the existing server admin key to browser JavaScript or use it as a user session.
- [x] Provide a documented one-time first-admin bootstrap procedure without shipping a default username or password.

## Premium Entitlement Storage and Rules

- [x] Add migrations for users, roles, sessions/tokens, and an auditable user-entitlement record containing entitlement type, start time, expiry time, revocation time, granting administrator, and timestamps. Do not model premium as an unaudited client-controlled boolean.
- [x] Derive the existing `tarot:deep_reading=true` authorization server-side only while the premium entitlement is active and the account is enabled.
- [x] Make expiry and revocation effective promptly; do not rely on a stale long-lived browser claim.
- [x] Check premium entitlement before ad-earned credits. Premium users must not consume rewarded-ad progress or credits when requesting DEEP.
- [x] Keep premium entitlement and rewarded DEEP credits separate so granting or revoking premium never corrupts reward history.
- [x] Store only identity and entitlement data required for account operation, document retention/deletion behavior, and never attach reading questions or generated answers to advertising records.

## API, Web, and Admin Work

- [x] Add authentication and account endpoints with consistent success/error envelopes, validation, rate-limit responses, and generated OpenAPI schemas.
- [x] Add admin-only user search, account status, premium grant/extend/revoke, and entitlement history endpoints with pagination and audit records.
- [x] Add accessible login/account pages and an admin premium-management interface; include loading, success, validation, expired-session, and forbidden states.
- [x] Update the reading-options response to expose only the current user's effective DEEP availability and premium expiry, never another user's entitlement details.
- [x] Update Privacy Policy, Terms, and account deletion documentation for user identity, authentication records, premium status, and retention.
- [x] Keep Swagger UI enabled in Development and disabled in Production unless explicitly secured, while keeping the generated OpenAPI document aligned with every new endpoint.

## Test and Acceptance Criteria

- [x] API tests cover registration policy, login, logout, current-user lookup, password hashing, verification/reset tokens, token expiry and replay, lockout, rate limits, CSRF, cookie flags, and session invalidation.
- [ ] Authorization tests prove anonymous and ordinary users cannot use DEEP or call admin endpoints, active premium users can use DEEP, and expired, revoked, disabled, or forged claims are rejected.
- [ ] Concurrency tests prove simultaneous premium updates are consistent and that premium DEEP requests never consume an ad-earned credit.
- [ ] Admin tests cover user creation, duplicate email rejection, role enforcement, premium grant/extension/revocation, expiry, pagination, and audit history.
- [ ] Browser tests cover Thai and English registration/login/logout/account flows, keyboard accessibility, invalid credentials, expired sessions, premium status changes, and DEEP button availability.
- [ ] Security tests confirm passwords, reset tokens, session cookies, admin credentials, and unrelated environment values never appear in responses, logs, OpenAPI examples, analytics, or advertising requests.
- [ ] Migration tests cover clean installation and upgrade of an existing production-shaped database without creating default credentials or granting premium access to existing users.

# Next Task — Immersive 3D Destiny Shop: Tarot Vertical Slice

Status: **Functional Phase 1 vertical slice implemented with procedural placeholder art; final asset production, Thai cultural review, measured performance budgets, audio, and future services remain planned**

## Implementation Checkpoint — 2026-09-06

- [x] Add an optional, dynamically loaded React Three Fiber shop to the localized Thai and English reading route.
- [x] Build a compact procedural room with a human Tarot advisor, consultation table, two chairs, cards, rug, shelves, books, lamps, plants, lighting, and a simple visitor avatar.
- [x] Support bounded WASD/arrow-key walking, touch/pointer movement controls, orbit camera control, advisor proximity feedback, advisor click interaction, and a visible exit.
- [x] Provide a direct “Start Tarot” path, WebGL capability fallback, reduced-motion handling, semantic HTML controls, and the complete existing 2D experience.
- [x] Hand consultation control to the existing `reading-client.tsx`, which remains connected to `/api/readings/options`, `/api/deck/shuffle`, `/api/deck/{sessionId}/resolve`, `/api/readings/generate`, authentication, and rewarded-DEEP access. No new endpoint was necessary for this vertical slice.
- [x] Add browser coverage for the direct accessible path and for walking to the advisor before entering the API-backed Tarot consultation.
- [x] Pass content validation, TypeScript checking, the production build, all 30 browser tests, and desktop/mobile visual inspection.

## Product Vision

Create a walkable 3D destiny shop where a visitor can explore the space, approach a human Thai spiritual advisor, sit at a consultation table, and choose a service. Phase 1 must deliver one complete, useful service—Tarot—before adding astrology, numerology, palm reading, or other practices.

The 3D experience is a presentation layer over the existing reading system. It must reuse the current Tarot API, account, STANDARD/DEEP entitlement, consent, safety, localization, and reading-state rules rather than creating a second reading implementation.

The shop should feel grounded, warm, and believable instead of like a fantasy game. Treat the advisor as a skilled human host, not a supernatural authority, and continue to describe readings as entertainment and self-reflection rather than guaranteed prediction or professional advice.

## Recommended Technical Direction

- [ ] Run a time-boxed prototype with `three`, React Three Fiber v9, and Drei because the web app already uses React 19. Do not add the dependencies to Production until the prototype passes the performance and accessibility gates.
- [ ] Add `@react-three/rapier` v2 only if the walkable prototype needs collision, ramps, or a physical character controller. Prefer simple bounded movement and authored colliders for the small Phase 1 room.
- [ ] Load the 3D shop as a client-only, route-level bundle. Keep account, legal, guide, admin, SEO, and initial server-rendered content outside the WebGL bundle.
- [ ] Keep the existing 2D reading journey as a first-class fallback and a visible “Start Tarot now” shortcut. Lack of WebGL, reduced motion, low device capability, keyboard-only use, or user preference must never block a reading.
- [ ] Keep scene state separate from business state. The 3D layer may request transitions such as `APPROACH_ADVISOR` or `OPEN_TAROT`, while the existing reading client remains authoritative for question, cards, API calls, errors, and results.

## Priority 0 — Experience, Cultural Review, and Prototype Gate

- [ ] Write the Phase 1 journey before producing final art: enter shop → understand available service → approach advisor or use shortcut → start Tarot → ask/select topic → shuffle and draw → receive reading → continue or leave.
- [ ] Confirm whether the camera is first-person or close third-person during the prototype. Test motion sickness, small screens, touch input, keyboard navigation, and discoverability before selecting one.
- [ ] Define one compact interior room, not an open world. Target a useful consultation within 30 seconds of entering the experience.
- [ ] Create a grey-box prototype containing only floor, walls, entrance, counter/table, advisor placeholder, interaction hotspot, camera, and movement boundaries.
- [ ] Test on representative low-, medium-, and high-capability phones plus desktop. Record frame time, memory, initial transferred bytes, loading time, battery/thermal behavior, and crash rate.
- [ ] Establish budgets before final art: compressed scene assets, texture memory, draw calls, active lights, shadow maps, and animation count. Provide Low, Standard, and High visual-quality profiles selected automatically with a manual override.
- [ ] Validate the concept with Thai cultural reviewers. Avoid mixing sacred Buddhist, Brahmin, animist, and commercial fortune-telling symbols as generic decoration. Document the meaning and approved use of every culturally specific costume, shrine, text, or ritual object.
- [ ] Do not copy a real practitioner’s face, voice, clothing, shop, or personal story without documented permission. Avoid exoticized labels or caricatures in Thai and English copy.

### Priority 0 Exit Gate

- [ ] A visitor can load the prototype, move or use the direct shortcut, locate the advisor, and open a placeholder Tarot panel on mobile and desktop.
- [ ] The experience works without pointer lock and provides an obvious exit from immersive mode.
- [ ] The 2D fallback remains fully functional when WebGL is disabled or the scene fails to load.
- [ ] Cultural review, art direction, target devices, and measurable performance budgets are approved before detailed models are commissioned.

## Priority 1 — Map and Navigation Blockout

- [ ] Design a small, legible floor plan with four zones: entrance/return point, service introduction, Tarot consultation table, and quiet result/reflection area.
- [ ] Use lighting, rug/floor treatment, furniture direction, and bilingual signage to guide the visitor toward the advisor without a minimap.
- [ ] Provide one collision-safe walking route wide enough for comfortable camera movement. Remove traps, narrow gaps, invisible steps, and decorative collision clutter.
- [ ] Add spawn, advisor interaction, sit/consult, result, and exit anchors as named scene nodes so interactions do not depend on mesh names or coordinates scattered through code.
- [ ] Add invisible simplified colliders for walls and large furniture. Decorative meshes must not be physics colliders.
- [ ] Support WASD and arrow keys, mouse/touch camera control, a mobile movement control, interaction key/button, and an always-visible “Start Tarot” shortcut.
- [ ] Prevent the camera from clipping through the advisor, table, walls, or cards. Restore a safe position when the player leaves the permitted area.

## Priority 1 — Required 3D Models and Assets

Create reusable Blender source files and optimized runtime GLB assets. Model in this order:

1. [ ] **Map shell:** floor, walls, ceiling, doors/windows, consultation alcove, and collision proxies.
2. [ ] **Consultation furniture:** correctly scaled Tarot table, two chairs, counter or service sign, rug, and a small storage cabinet.
3. [ ] **Advisor character:** respectful Thai adult human with neutral idle, greeting, seated, listening, card-handling, and result-presenting poses. Start with a licensed or custom low-poly base and validate skin tone, clothing, proportions, and gestures with human reviewers.
4. [ ] **Tarot hero props:** deck, card back, face-card material/atlas, table cloth, draw positions, spread positions, and one readable service menu.
5. [ ] **Lighting props:** practical lamps and restrained candles/incense only where culturally and physically appropriate; provide non-particle and low-quality variants.
6. [ ] **Atmosphere props:** shelves, books, plants, curtains, framed artwork, containers, and small objects used to tell a coherent shop story.
7. [ ] **Optional player representation:** hands or a simple avatar only after camera and interaction testing proves it improves presence.

For every asset:

- [ ] Record creator, source, license, permitted modifications, attribution, cultural-review status, polygon count, materials, texture sizes, animations, and final file size in an asset manifest.
- [ ] Use real-world scale, consistent origins, named nodes, baked transforms, LODs where useful, shared materials, and compressed textures. Strip unused cameras, lights, bones, and animation tracks before export.
- [ ] Keep selectable Tarot card faces crisp enough to recognize while avoiding dozens of unique high-resolution materials; prefer an atlas or controlled texture-loading strategy.
- [ ] Never ship unlicensed marketplace assets, fonts, music, voices, tarot artwork, or AI-generated likenesses with unclear commercial rights.

## Priority 1 — Connect the Existing API

- [ ] Refactor the current `reading-client.tsx` request/state orchestration into a shared Tarot flow controller or hook used by both the existing 2D interface and the new 3D scene. Do not copy the fetch sequence, validation, or entitlement logic into scene components.
- [ ] On consultation startup, call `GET /api/readings/options` with browser credentials to load server-authoritative reading modes, DEEP entitlement, ad-earned credit availability, upgrade URL, and available model tiers.
- [ ] Start every draw with `POST /api/deck/shuffle` using the selected spread. Store the returned `sessionId`, spread, and selectable card count in shared flow state; do not shuffle or manufacture a trusted deck solely inside Three.js.
- [ ] After the visitor selects the required 3D card indexes, call `POST /api/deck/{sessionId}/resolve`. Use only the returned card IDs, positions, and orientations as the authoritative revealed cards.
- [ ] Generate the result with `POST /api/readings/generate`, passing the existing contract fields: composed question, resolved spread, locale, reading mode, DEEP model tier when applicable, and the exact resolved cards.
- [ ] Keep `credentials: "include"`, the existing same-origin/API-base behavior, response-envelope parsing, CSRF/session behavior where applicable, request cancellation, stale-response protection, retry rules, and localized errors.
- [ ] Reuse the existing authentication and rewarded-DEEP components and endpoints. The 3D client must never create entitlement flags, grant credits, store bearer tokens, or decide whether a DEEP request is authorized.
- [ ] Drive scene animation from shared states such as `IDLE`, `SHUFFLING`, `SELECTING`, `RESOLVING`, `GENERATING`, `REVEALING`, `COMPLETE`, and `ERROR`; scene animation must react to API state and must not replace it.
- [ ] Preserve the server-provided order and identity of cards when placing them on the 3D table. Selecting a 3D mesh records only its selectable index until the resolve endpoint returns the real card.
- [ ] Reuse existing endpoints where their contracts fit, but freely add or revise an HTTP endpoint when the 3D experience needs a cleaner contract, better performance, stronger security, or simpler recovery. Do not force the 3D client through an unsuitable endpoint merely to avoid backend work. Keep backward compatibility for the existing 2D client or migrate both clients together. Update controller response annotations, generated OpenAPI schemas, the Development Swagger UI, API contract tests, and frontend runtime validation in the same change.
- [ ] Add contract tests proving that the 2D and 3D adapters create equivalent requests and interpret the same success, validation, forbidden, missing-session, and upstream-failure responses.

## Priority 1 — Complete Tarot Consultation Loop

- [ ] Show a clear bilingual interaction prompt when the visitor enters the advisor hotspot. Do not depend on hovering over a small 3D object.
- [ ] Transition to a stable consultation camera and accessible HTML interface when the visitor chooses Tarot. Lock walking input while the consultation UI is active and restore it safely when closed.
- [ ] Reuse the current Thai/English topic, spread, reading-mode, question, shuffle, draw, resolve, generate, reveal, error, retry, and reset behavior.
- [ ] Represent selected cards on the 3D table, but keep equivalent card names, positions, orientations, explanations, status, and controls in semantic HTML for screen readers and keyboard users.
- [ ] Synchronize advisor animation to existing reading phases: greet/idle, shuffle, deal, wait/listen, reveal, present result, and recover from error. Animation timing must never delay or fabricate an API result.
- [ ] Present the full structured reading in accessible HTML. The 3D advisor may introduce or highlight it, but must not be the only way to read, copy, scroll, or retry the result.
- [ ] Preserve all current rules for STANDARD, premium DEEP, rewarded access, authentication, privacy, and safe language. Do not send camera, movement, device, or scene data to the reading API.
- [ ] Keep the consultation and reading-result areas free of regular display ads. A rewarded ad may appear only through the existing explicit, consent-gated unlock flow.

## Priority 2 — Visual, Audio, and Interaction Polish

- [ ] Replace the approved blockout with final modular art without moving interaction anchors or changing the tested route.
- [ ] Add restrained baked lighting, contact shadows, ambient motion, dust or smoke only on capable devices, and color grading that preserves card and text readability.
- [ ] Blend advisor animations and add natural eye/head attention without uncanny tracking. Never imply that the camera or microphone is observing the visitor.
- [ ] Add optional ambient sound and advisor voice only after licensing, Thai pronunciation, localization, consent, mute, volume, caption, and autoplay behavior are approved. Default to captions and never require audio.
- [ ] Add focus indicators, interaction feedback, loading progress, network-loss recovery, scene-reload recovery, and a direct return to the normal site.
- [ ] Measure whether 3D improves Tarot starts, completion, return visits, and user satisfaction. Do not optimize for time trapped in the scene.

## Priority 3 — Future Services After Tarot Proves the Platform

- [ ] Add a data-driven service catalog so unavailable services can be shown as “coming later” without pretending that they work.
- [ ] Evaluate one future service at a time—such as Thai astrology, numerology, palm reading, or dream reflection—with its own subject-matter, cultural, safety, privacy, API, and monetization review.
- [ ] Reuse the same shop, advisor interaction contract, accessibility shell, loading system, and entitlement boundary. Do not fork a new movement engine or duplicate account logic per service.
- [ ] Add additional rooms, practitioners, multiplayer, persistent avatars, inventory, or an outdoor map only after analytics show that the compact single-room experience is valuable and its performance budget is stable.

## Testing and Acceptance Criteria

- [ ] Browser tests cover supported desktop and mobile controls, interaction prompts, consultation entry/exit, direct shortcut, route changes, API errors, reload recovery, and the complete Tarot state machine in Thai and English.
- [ ] Accessibility tests cover keyboard-only completion, visible focus, screen-reader reading flow, reduced motion, high zoom, captions, color contrast, and the 2D fallback.
- [ ] Performance tests enforce the approved asset, memory, frame-rate, load-time, and draw-call budgets for Low, Standard, and High profiles.
- [ ] Visual tests cover camera collision, aspect ratios, safe areas, text/card legibility, model clipping, animation transitions, missing assets, and low-quality rendering.
- [ ] Security and privacy tests confirm 3D analytics never contain questions, readings, chosen cards/topics, account identity, entitlement data, or precise behavioral replays of the consultation.
- [ ] Asset audit confirms commercial rights, attribution, optimization, and cultural-review records for everything shipped.
- [ ] Existing API, account, monetization, content, sitemap, admin, and 2D reading tests remain green.
- [ ] No new HTTP endpoint is added unless its request/response schemas are included in generated OpenAPI and verified in Development Swagger UI, which remains unavailable in Production unless explicitly secured.

## Phase 1 Definition of Done

- [ ] A first-time visitor can enter the 3D shop, understand that Tarot is available, reach the advisor, complete a real STANDARD or authorized DEEP reading, review the result, and leave or restart without confusion.
- [ ] The same end-to-end Tarot reading remains possible through the direct 2D path on every supported device.
- [ ] The advisor, room, furniture, cards, animation, lighting, and audio that ship have documented ownership and cultural approval.
- [ ] The experience meets the agreed accessibility and performance budgets and introduces no regression to current reading, authentication, privacy, or monetization behavior.
- [ ] Other spiritual services remain explicitly out of Phase 1 and are not implemented until the Tarot vertical slice is measured and approved.
