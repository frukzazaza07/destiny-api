# SUMMARY.md — Tarot Destiny System Context

## Purpose of This Document

This document provides the high-level context for the Tarot Destiny project.

Another developer or AI agent should read this file **before** reading `TASK.md`.

`SUMMARY.md` explains:

- What the product is trying to build
- Why the architecture is designed this way
- How Tarot readings are generated
- How LLM inference is used
- Why caching is important
- How question classification improves cache reuse
- How the personal GPU computer fits into the system
- Recommended infrastructure and tech stack

Detailed implementation work belongs in `TASK.md`.

---

# 1. Product Goal

Build a Tarot-based destiny / guidance application where a user:

1. Chooses a reading type
2. Selects a topic or asks a question
3. Randomly selects Tarot cards
4. Receives an AI-generated reading
5. Can save, review, or continue discussing the reading

The application should feel personal and meaningful, but the system should avoid presenting Tarot as guaranteed factual prediction.

The preferred framing is:

```text
Tarot provides symbolic themes and possibilities.
The system turns those symbols into a coherent story and practical reflection.
```

Instead of:

```text
The system knows exactly what will happen in the future.
```

---

# 2. Suggested User Experience

Basic flow:

```text
Start
  ↓
Choose Reading Type
  ↓
Choose Topic / Ask Question
  ↓
Shuffle
  ↓
Select Cards
  ↓
Reveal Cards
  ↓
Interpret Cards
  ↓
Generate Reading
  ↓
Display Result
  ↓
Save / Share / Follow Up
```

Recommended initial reading modes:

```text
1 Card
→ Daily / Quick Guidance

3 Cards
→ Past
→ Present
→ Possible Direction

7 Cards
→ Deep Reading
```

The 3-card spread is the recommended primary MVP experience.

---

# 3. Suggested Question Topics

Users may choose predefined topics:

```text
General
Love
Career
Money
Family
Personal Growth
```

Or ask a free-form question.

Example:

```text
Should I change my job?
```

The system should not require free-text questions for every reading.

Predefined topics improve:

- Simplicity
- Cache hit rate
- Classifier accuracy
- Consistency

---

# 4. Tarot Deck Model

Standard Tarot deck:

```text
78 cards

22 Major Arcana
56 Minor Arcana
```

Each draw has an orientation:

```text
UPRIGHT
REVERSED
```

Recommended data representation:

```json
{
  "cardId": "THE_MAGICIAN",
  "orientation": "UPRIGHT"
}
```

Orientation should not be modeled as a separate physical card.

---

# 5. Random Card Selection

The backend should control the actual shuffled deck.

Recommended flow:

```text
Backend creates shuffled 78-card deck
       ↓
Frontend shows hidden positions
       ↓
User selects positions
       ↓
Backend resolves selected positions to cards
```

This prevents the frontend from deciding or manipulating the actual card identity.

For .NET, prefer cryptographically secure randomization for deck shuffling.

Example concept:

```text
RandomNumberGenerator
```

rather than relying only on browser-side random selection.

---

# 6. Tarot Knowledge Base

Do not rely on the LLM to invent Tarot meanings from scratch.

Store Tarot card meanings in structured master data.

Example:

```json
{
  "id": "THE_MAGICIAN",
  "name": "The Magician",
  "arcana": "MAJOR",
  "element": "AIR",

  "upright": {
    "keywords": [
      "skill",
      "initiative",
      "manifestation"
    ],
    "general": "...",
    "career": "...",
    "love": "...",
    "money": "..."
  },

  "reversed": {
    "keywords": [
      "poor planning",
      "untapped potential"
    ],
    "general": "...",
    "career": "...",
    "love": "...",
    "money": "..."
  }
}
```

This knowledge base becomes the application's source of truth.

The LLM should mainly synthesize and phrase the reading.

---

# 7. Tarot Reading Logic

A good Tarot reading should not be:

```text
Card 1 meaning
Card 2 meaning
Card 3 meaning
```

The system should combine:

```text
Card meaning
+
Orientation
+
Spread position
+
Question topic
+
Relationships between cards
+
Overall spread narrative
```

Example:

```text
Past       → The Tower
Present    → The Magician
Direction  → The Star
```

Potential story:

```text
Previous disruption
      ↓
Current ability and initiative
      ↓
Possible renewal and hopeful direction
```

The LLM should synthesize this into one coherent reading.

---

# 8. Rule Engine vs LLM

Core architectural principle:

```text
Rule Engine = decides what the reading means

LLM = decides how to say it
```

The backend should prepare structured information before calling the LLM.

Example:

```json
{
  "topic": "CAREER",
  "mainTheme": "transformation",
  "opportunity": "new beginning",
  "challenge": "letting go of old structure",
  "direction": "long-term stability"
}
```

Then the LLM converts this into natural language.

Benefits:

- Lower token usage
- Faster inference
- Better consistency
- Easier caching
- Easier prompt versioning
- Less hallucination

---

# 9. LLM Is the Main Runtime Bottleneck

Most Tarot operations are inexpensive:

```text
Shuffle deck
Database lookup
Card rules
JSON processing
Question classification
Cache lookup
```

The expensive component is:

```text
LLM inference
```

Important LLM limitations:

- GPU VRAM
- Generation latency
- Concurrent requests
- Prompt length
- Output length
- GPU utilization
- Queue depth

Therefore the architecture should try to avoid unnecessary LLM calls.

---

## Standard And Premium Deep Reading Modes

The user chooses the generation mode before revealing the reading:

```text
STANDARD
→ Rule-engine reading
→ No LLM call
→ Available without a premium entitlement

DEEP
→ Premium option
→ Rule interpretation + private qwen3:8b synthesis through Ollama
→ Requires a backend-verified entitlement
```

The browser must never be trusted to declare that a user has paid. In production, authentication or billing infrastructure grants the server-side claim `tarot:deep_reading=true`; the ASP.NET backend enforces that claim before any Deep generation. Development may enable an explicit local bypass for testing.

Both modes return the same structured reading contract and may use finished-answer caching. Cache identity must include the reading mode so a Standard rule response can never collide with a Deep LLM response. Deep cache identity also includes the configured model and prompt versions.

If Deep inference returns malformed JSON, changes card identity/order, or uses the wrong output language, validation rejects it and the response is not cached.

---

# 10. Finished LLM Answer Cache

The main optimization idea is:

```text
If a new user receives the same effective question type
and the same Tarot spread/cards,
reuse a previously generated finished LLM answer.
```

Basic flow:

```text
User Question
    ↓
Question Classifier
    ↓
Question Type
    ↓
Selected Cards
    ↓
Build Cache Key
    ↓
Redis
   ↙   ↘
 HIT   MISS
 ↓      ↓
Return  LLM
cache    ↓
       Store Result
          ↓
       Return
```

The cache should store the **finished structured generated response**, with Standard and Deep entries kept separate by reading mode.

---

# 11. Why Classify Questions First

Caching against exact user question text would produce poor cache reuse.

These questions have similar intent:

```text
Should I change my job?

Should I leave my company?

Is it time to find another job?

What happens if I move to another company?
```

They should ideally classify to:

```text
Domain: CAREER
Intent: CAREER_CHANGE_JOB
```

Then users with the same question intent and same selected cards can share a finished cached answer.

---

# 12. Question Classification Design

Recommended classifier output:

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

Important fields:

```text
domain
intent
confidence
personalization
source
modelVersion
```

Example domains:

```text
GENERAL
LOVE
CAREER
MONEY
FAMILY
PERSONAL_GROWTH
```

Example intents:

```text
CAREER_GENERAL
CAREER_NEW_JOB
CAREER_CHANGE_JOB
CAREER_PROMOTION
CAREER_BUSINESS
CAREER_DECISION

LOVE_GENERAL
LOVE_SINGLE
LOVE_RELATIONSHIP
LOVE_BREAKUP
LOVE_RECONCILIATION
LOVE_COMMITMENT

MONEY_GENERAL
MONEY_INCOME
MONEY_INVESTMENT
MONEY_BUSINESS
MONEY_DEBT
MONEY_DECISION
```

Recommended initial taxonomy:

```text
Approximately 20–30 intents
```

Too few intents:

```text
High cache hit rate
but poor relevance
```

Too many intents:

```text
Very specific answers
but low cache hit rate
```

The goal is a practical middle ground.

## Classifier Service Boundary

Free-text classification runs in a private Python CPU service called by the ASP.NET backend over unary gRPC.

```text
ASP.NET Backend
    -> gRPC deadline
Python Classifier
    -> domain + intent + confidence + personalization + model version
ASP.NET Policy
    -> validate taxonomy
    -> raise personalization when local safety rules are stricter
    -> apply confidence threshold
    -> decide shared-cache eligibility
```

Python owns feature extraction, model loading, and prediction. C# owns business and privacy policy. The Python service must never build cache keys or decide whether a finished answer may be shared.

Predefined topic selections remain deterministic in C# and do not require a network call. If the Python service times out, is unavailable, or returns an invalid taxonomy value, C# falls back conservatively and must not make the reading endpoint unavailable.

The implemented baseline uses a versioned `tfidf-logreg-seed-v1` artifact, bilingual seed examples for every learned intent, standard gRPC health checks, a 500 ms configurable deadline, and loopback-only Docker exposure for local development. Classifier source, model version, remote latency, and fallback count are observable without logging raw questions.

---

# 13. Personalized Questions

Not every question should reuse a generic cached answer.

Example:

```text
I have worked with my brother for 8 years and we are considering
selling our company because he plans to move overseas.
What should I do?
```

This contains detailed personal context.

The classifier should be able to mark:

```text
personalization = HIGH
```

Then:

```text
Skip shared finished-answer cache
       ↓
Generate directly by reading mode
```

This prevents inappropriate reuse.

---

# 14. Classifier Confidence

The classifier should return a confidence score.

Example:

```json
{
  "domain": "CAREER",
  "intent": "CAREER_CHANGE_JOB",
  "confidence": 0.95
}
```

If confidence is high:

```text
Use classification
→ eligible for shared cache
```

If confidence is low:

```text
Treat as PERSONAL_CUSTOM
→ skip shared finished-answer cache
```

Recommended starting threshold:

```text
0.85
```

This value should be configurable and tuned using real data.

---

# 15. Classifier Evolution

Recommended evolution:

```text
Stage 1
C# rules and explicit topics

Stage 2
Python TF-IDF + Logistic Regression over gRPC, with C# fallback

Stage 3
Collect real user questions and reviewed labels

Stage 4
Train custom classifier
```

For MVP, a lightweight model may be enough.

Possible options:

```text
TF-IDF + Logistic Regression
```

or later:

```text
Sentence Transformer
+
Small classification head
```

The classifier should run on CPU.

Do not waste the main LLM GPU on simple question classification.

The seed model is an architectural baseline, not a claim of production accuracy. Until reviewed production labels exist, low-confidence predictions must become `PERSONAL_CUSTOM` and skip shared finished-answer cache.

---

# 16. Cache Key Concept

The shared finished-answer cache key should depend on:

```text
Question domain
Question intent
Reading mode
Spread type
Card position
Card identity
Card orientation
Locale
Prompt version
Interpretation version
```

Required for Deep mode:

```text
Model version
```

Example canonical input:

```text
CAREER
CAREER_CHANGE_JOB
DEEP
DESTINY_3
TH
PAST:THE_TOWER:UPRIGHT
PRESENT:THE_MAGICIAN:UPRIGHT
DIRECTION:THE_STAR:UPRIGHT
PROMPT_V3
INTERPRETATION_V2
```

Then hash:

```text
SHA256(...)
```

Redis key:

```text
tarot:answer:<hash>
```

---

# 17. Card Position Must Affect Cache Identity

These are different readings:

```text
Past      = Tower
Present   = Magician
Direction = Star
```

and:

```text
Past      = Star
Present   = Magician
Direction = Tower
```

Even though the same cards are present.

Cache identity must include:

```text
position
+
card
+
orientation
```

Do not sort cards alphabetically when building the cache key.

---

# 18. Prompt Versioning

Prompt version must be part of cache identity.

Example:

```text
PROMPT_V1
PROMPT_V2
PROMPT_V3
```

If prompt behavior changes:

```text
PROMPT_V3
→ PROMPT_V4
```

old cached responses naturally stop matching.

This is better than manually clearing all previous cached entries.

---

# 19. Interpretation Versioning

Tarot meanings may also evolve.

Example:

```text
INTERPRETATION_V1
INTERPRETATION_V2
```

Include this version in the cache key.

If Tarot master content changes, new readings automatically use a new cache namespace.

---

# 20. Structured LLM Output

The LLM should return structured JSON rather than arbitrary Markdown.

Example:

```json
{
  "title": "A New Direction Is Opening",
  "summary": "...",
  "mainTheme": "...",

  "cards": [
    {
      "position": "PAST",
      "cardId": "THE_TOWER",
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

- Easy frontend rendering
- Easy validation
- Easy caching
- Easier API versioning
- Better localization support
- Easier future prompt changes

---

# 21. Cache Layers

The main requested optimization is finished-answer caching, but several cache levels are possible.

## L1 — Tarot Master / Card Meaning Cache

Very high reuse.

Example:

```text
THE_MAGICIAN
UPRIGHT
CAREER
PRESENT
TH
```

Usually backed by PostgreSQL and optionally Redis.

---

## L2 — Spread / Base Interpretation Cache

Stores reusable interpretation of a specific card spread.

Example:

```text
CAREER
Tower Upright
Magician Upright
Star Upright
```

May contain:

```text
mainTheme
challenge
opportunity
direction
```

---

## L3 — Finished LLM Answer Cache

Primary optimization for this project.

Same:

```text
question intent
+
spread
+
ordered cards
+
orientation
+
locale
+
versions
```

can return a previously generated complete reading without calling the LLM.

---

# 22. Cache Hit vs Cache Miss

Cache HIT:

```text
Same effective intent
Same spread
Same card positions
Same cards
Same orientations
Same locale
Same prompt version
Same interpretation version
```

Result:

```text
Return cached finished response
LLM GPU not used
```

Cache MISS:

```text
Any cache identity input differs
```

Result:

```text
Generate by reading mode
Validate
Save result
Return
```

Skip shared cache:

```text
Low classifier confidence
or
Highly personalized question
```

Result:

```text
Generate directly by reading mode
```

---

# 23. Cache Growth Strategy

The cache can grow naturally from production traffic.

Example:

```text
First request
→ MISS
→ Generate with LLM
→ Cache result

Second matching request
→ HIT
→ No LLM
```

Over time:

```text
Traffic creates a library of common Tarot readings.
```

This is especially useful for common question types and popular spreads.

---

# 24. One-Card Readings Are Highly Cacheable

Example possible combinations:

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

This is small enough to potentially pre-generate offline.

One-card readings could eventually require almost no runtime LLM inference.

---

# 25. Three-Card Readings

Three-card combinations are much larger.

Do not pre-generate all possible combinations.

Recommended:

```text
Lazy generation
```

Flow:

```text
First real request
→ Generate
→ Cache

Later matching request
→ Reuse
```

---

# 26. Multiple Cached Variants

One future optimization is to store multiple wording variants for the same cache identity.

Instead of:

```text
Cache Key
→ One answer
```

use:

```text
Cache Key
→ Variant A
→ Variant B
→ Variant C
```

On cache hit:

```text
Randomly choose one
```

Benefits:

- No GPU usage
- Less visible repetition
- Different users can receive slightly different wording

This is optional, not required for MVP.

---

# 27. Redis vs PostgreSQL

Recommended responsibilities:

```text
Redis
= Fast runtime cache

PostgreSQL
= Persistent generated reading library
```

Preferred lookup flow:

```text
Redis
 ↓ MISS
PostgreSQL
 ↓ MISS
LLM
 ↓
Save PostgreSQL
 ↓
Save Redis
 ↓
Return
```

If Redis is flushed, the system can repopulate cache from PostgreSQL.

Generated LLM answers become reusable content assets rather than disposable cache entries.

---

# 28. Cache Stampede Problem

If many users request the same uncached reading at the same time:

```text
100 users
→ same cache key
→ 100 misses
→ 100 LLM calls
```

This wastes GPU.

The system should eventually use a distributed lock:

```text
MISS
 ↓
Lock cache key
 ↓
Check cache again
 ↓
Generate only once
 ↓
Save
 ↓
Release lock
```

Redis can be used for this.

---

# 29. GPU Computer Role

The personal GPU computer should be a dedicated inference worker.

It should mainly run:

```text
Ubuntu
Docker
NVIDIA Container Toolkit
vLLM
Tailscale
LLM model
```

It does not need to host:

```text
Public website
Business API
PostgreSQL
Redis
Authentication
Tarot business logic
```

Treat it as:

```text
Private LLM Inference Worker
```

---

# 30. Do Not Expose the GPU PC Directly

Avoid:

```text
Internet
   ↓
Public IP
   ↓
vLLM :8000
```

or:

```text
Internet
   ↓
Ollama :11434
```

This creates unnecessary risk.

Someone could discover the endpoint and consume:

- GPU
- VRAM
- Electricity
- Bandwidth
- Inference capacity

---

# 31. Recommended Private Networking

Preferred architecture:

```text
Public Backend / VPS
        │
        │ Tailscale
        ▼
Personal GPU PC
        │
        ▼
vLLM
```

The backend calls the GPU computer through a private VPN IP.

Example:

```text
http://100.x.x.x:8000/v1/chat/completions
```

Only authorized VPN nodes can access the inference endpoint.

---

# 32. 3BB Thailand / Public IP

A public IPv4 on the home Internet connection is **not required** for the recommended setup.

Tailscale works even if the 3BB connection uses:

- CGNAT
- Private IPv4
- Dynamic IP
- NAT

The GPU computer initiates an outbound connection to the VPN network.

Therefore the recommended solution normally does not require:

```text
3BB Public IPv4
Router port forwarding
DDNS
Open inbound router ports
```

If the system later becomes a high-volume commercial service, review the ISP plan and terms and consider a suitable business-grade connection.

---

# 33. Direct Public Exposure Alternative

Technically possible, but not preferred.

Would require:

```text
Public IPv4
Port forwarding
Firewall
HTTPS
Authentication
Rate limiting
DDNS if dynamic
Monitoring
Security patching
```

Architecture:

```text
Internet
 ↓
3BB Public IPv4
 ↓
Router
 ↓
Reverse Proxy
 ↓
GPU LLM Server
```

This is more complex and less safe than using a private VPN.

---

# 34. Recommended Tech Stack

## Frontend

```text
Next.js
TypeScript
```

Responsibilities:

- Tarot UI
- Card selection
- Animations
- Reading display
- Reading history
- User interaction

---

## Backend

```text
ASP.NET Core
EF Core
Npgsql
StackExchange.Redis
```

Responsibilities:

- Authentication
- Tarot rules
- Random card selection
- Question classification orchestration
- Cache key generation
- Redis lookup
- PostgreSQL persistence
- LLM calls
- Response validation
- Rate limiting
- Metrics

---

## Database

```text
PostgreSQL
```

Store:

```text
Tarot card master data
Localized meanings
Spreads
Spread positions
Reading history
Generated answer library
Classifier training data
```

---

## Cache

```text
Redis
```

Use for:

```text
Finished-answer cache
Card/master cache
Distributed locking
Rate limiting
Short-lived session data
Optional queue
```

---

## Classifier

Recommended:

```text
Python
gRPC / Protocol Buffers
scikit-learn
TF-IDF + Logistic Regression
ASP.NET gRPC client
C# rule fallback and cache-safety policy
```

MVP model option:

```text
TF-IDF
+
Logistic Regression
```

Later:

```text
sentence-transformers
+
Small classifier head
```

Run classifier on CPU.

---

## LLM Serving

Development:

```text
Ollama
```

Production / higher concurrency:

```text
vLLM
```

vLLM is preferred because it supports:

- Efficient inference serving
- Better batching
- Better concurrency
- OpenAI-compatible API

Example endpoint:

```text
POST /v1/chat/completions
```

---

## Model

Good initial candidates:

```text
Qwen
Llama
```

Test models in approximately:

```text
7B / 8B
14B
```

For this project, a 14B-class model may provide a good quality / hardware balance if the GPU supports it.

Model choice should be validated for:

```text
Thai quality
English quality
JSON reliability
Latency
VRAM usage
Concurrent throughput
```

---

## VPN

Recommended:

```text
Tailscale
```

Alternative:

```text
WireGuard
```

---

## Reverse Proxy / Public Edge

Recommended:

```text
Cloudflare
```

Optional internal reverse proxy:

```text
Nginx
or
Caddy
```

---

## Container Runtime

Recommended:

```text
Docker
Docker Compose
```

Backend server:

```text
backend
classifier
redis
postgres
monitoring
```

GPU server:

```text
vllm
monitoring agent
```

---

## Queue

Do not introduce Kafka for MVP.

Start with:

```text
ASP.NET concurrency limiter
```

Later:

```text
Redis Streams
```

or:

```text
RabbitMQ
```

if queued inference becomes necessary.

---

# 35. Recommended Overall Architecture

```text
                        USERS
                          │
                          ▼
                    Cloudflare
                          │
                          ▼
                ┌─────────────────┐
                │ ASP.NET Backend │
                └────────┬────────┘
                         │
             ┌───────────┼───────────┐
             │           │           │
             ▼           ▼           ▼
         PostgreSQL    Redis      Classifier
                                   Python
                                   gRPC
                                   CPU
                                      │
                                      ▼
                               Cache Eligible?
                                      │
                              ┌───────┴───────┐
                              │               │
                             YES              NO
                              │               │
                              ▼               │
                            Redis             │
                         ↙         ↘           │
                       HIT         MISS        │
                        │            │         │
                        │            ▼         │
                        │       PostgreSQL     │
                        │        Answer DB     │
                        │            │         │
                        │           MISS       │
                        │            │         │
                        │            └────┬────┘
                        │                 ▼
                        │             Tailscale
                        │                 │
                        │                 ▼
                        │        ┌────────────────┐
                        │        │ Personal GPU   │
                        │        │ vLLM           │
                        │        │ Qwen / Llama   │
                        │        └───────┬────────┘
                        │                │
                        │                ▼
                        │          LLM Response
                        │                │
                        │       Save DB + Redis
                        │                │
                        └──────────┬─────┘
                                   ▼
                               User Result
```

---

# 36. Recommended Physical Deployment

## Public Backend / VPS

Run:

```text
ASP.NET Core
PostgreSQL
Redis
Classifier
Monitoring
Tailscale
```

## Personal GPU Computer

Run:

```text
Ubuntu
NVIDIA Driver
Docker
NVIDIA Container Toolkit
vLLM
Qwen / Llama
Tailscale
```

Connection:

```text
Backend VPS
   ↓
Private Tailscale Network
   ↓
GPU PC
```

No direct public GPU endpoint is needed.

---

# 37. Monitoring

Important metrics:

```text
Total reading requests

Classifier latency
Classifier confidence
Classifier rejection rate

Cache eligible requests
Cache hits
Cache misses
Cache hit rate

LLM requests avoided
LLM requests executed

LLM latency
Input tokens
Output tokens
Queue length

GPU utilization
GPU VRAM
GPU temperature

Errors
Timeouts
Retries
```

Important business/technical metric:

```text
Cache Hit Rate
=
Cache Hits / Cache Eligible Requests
```

Also measure:

```text
LLM Avoidance Rate
=
Requests served without LLM / Total reading requests
```

---

# 38. Expected Scaling Behavior

At low traffic:

```text
Many MISS
→ More LLM calls
→ Cache gradually grows
```

At higher traffic:

```text
Common question intents repeat
Common card combinations repeat
→ More HITs
→ Less GPU work per user
```

The system should gradually build a reusable library of common generated Tarot readings.

---

# 39. Future Product Features

After the core reading system works, possible features include:

```text
Daily Tarot
Tarot Journal
Reading History
Recurring Card Analysis
Repeated Theme Analysis
7-Card Deep Reading
Follow-Up AI Conversation
Shareable Reading
Multiple Cached Variants
Numerology
Astrology
Zodiac
Moon Phase
```

A strong retention feature could be:

```text
Your Tarot Journey
```

Example:

```text
Recent cards:
The Hermit
Death
The Star

Pattern:
Reflection
→ Transformation
→ Renewal
```

This should be presented as recurring symbolic themes rather than guaranteed destiny.

---

# 40. Product Safety / Tone

The LLM should avoid:

```text
Guaranteed future predictions
Death predictions
Medical diagnosis
Guaranteed pregnancy claims
Guaranteed financial outcomes
Legal conclusions
```

Preferred language:

```text
may
suggests
points toward
possible direction
theme
pattern
opportunity
challenge
reflection
```

The reading should still feel engaging and meaningful without claiming certainty.

---

# 41. Core Design Principles

Keep these principles throughout implementation:

```text
1. Tarot master data is the source of truth.

2. Rule Engine decides meaning.
   LLM decides wording.

3. LLM inference is expensive.
   Avoid unnecessary GPU calls.

4. Classify question intent before cache lookup.

5. Reuse finished readings when the effective intent
   and card spread are the same.

6. Do not reuse generic answers for highly personalized questions.

7. Card position and orientation matter.

8. Version prompts and Tarot interpretation content.

9. Redis is for speed.
   PostgreSQL is for persistence.

10. GPU server stays private.

11. Backend is the only public application service.

12. Start simple and add complexity only when real traffic proves the need.
```

---

# 42. Current Recommended Stack Summary

```text
Frontend
    Next.js + TypeScript

Backend
    ASP.NET Core
    EF Core
    Npgsql

Database
    PostgreSQL

Cache
    Redis
    StackExchange.Redis

Classifier
    Python
    gRPC
    Protocol Buffers
    scikit-learn
    TF-IDF + Logistic Regression
    CPU inference

Classifier Policy / Fallback
    ASP.NET Core
    Taxonomy validation
    Confidence threshold
    Personalization safety guard

LLM Server
    vLLM

Development LLM Option
    Ollama

Model
    Qwen / Llama
    Test 7B–14B class first

GPU
    NVIDIA

Private Network
    Tailscale

Public Edge
    Cloudflare

Containers
    Docker
    Docker Compose

Monitoring
    Prometheus
    Grafana

Logging
    Serilog
    Loki

Future Queue
    Redis Streams
    or RabbitMQ
```

---

# 43. Read Next

After understanding this document, read:

```text
TASK.md
```

`TASK.md` contains the implementation-oriented work breakdown, cache rules, classifier contract, Redis behavior, persistence design, success criteria, and phased checklist.
