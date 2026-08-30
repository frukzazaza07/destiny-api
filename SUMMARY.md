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
- How question classification improves STANDARD-mode cache reuse
- How the personal GPU computer fits into the system
- Recommended infrastructure and tech stack

Detailed implementation work belongs in `TASK.md`.

---

# Frontend Reading Journey Update (2026-08-30)

The main reading page now uses an explicit interaction phase instead of a shared `busy` boolean:

```text
IDLE → SHUFFLING → SELECTING → RESOLVING → GENERATING → REVEALING → COMPLETE
```

Network failures enter `ERROR` with a retry scoped to the failed shuffle, resolve, or generation step. The implementation preserves the question and settings, keeps selected indexes after a resolve failure, and keeps resolved cards visible after a generation failure.

Current frontend behavior:

- Shuffle requests run alongside a staged `MIXING → SETTLING → DEALING` sequence. After the real session is ready, the motion visibly decelerates into one stack. Cards then peel from the top one at a time, lift through a shallow natural arc, rotate with their travel direction, and land flat in the responsive grid; slow requests remain in the mixing treatment until the backend responds.
- Selection shows gold lift/glow feedback and the selection order (`1`, `2`, `3`) while retaining button, focus, disabled, and `aria-pressed` semantics.
- Resolve and generation replace the full deck with the selected cards in spread positions. Localized English/Thai status text is exposed through one polite live region, and the reading region uses `aria-busy` during network work.
- `STANDARD` has a 300 ms minimum generation treatment to prevent a flash. `DEEP` uses the same stable, bounded loader without growing particles, timers, or DOM content.
- Successful responses enter the DOM immediately in `REVEALING`, flip in spread order, then reveal summary and insights. Reversed orientation is represented by the card treatment without implying a negative meaning.
- Completed-reading focus moves to the result heading and scrolls only when needed.
- Resetting or changing settings aborts the active request and increments a flow version, preventing obsolete responses or timers from overwriting a newer reading.
- `prefers-reduced-motion: reduce` removes shuffle travel, looping effects, stagger, and 3D flips. State completion uses cleaned-up timers rather than animation events.
- Existing request bodies and the backend `STANDARD`/`DEEP` boundaries are unchanged.

Verification completed:

```text
apps/web: npm run typecheck → passed
apps/web: npm run build     → passed (Next.js 16.3.2)
```

Live browser QA and screenshots remain pending because the in-app browser was unavailable in the implementation session.

---

# สรุปภาษาไทย — สถานะระบบที่ใช้งานจริง (อ่านส่วนนี้ก่อน)

ส่วนนี้คือคำอธิบายภาษาไทยของพฤติกรรมปัจจุบันของ `/api/readings/generate` หลังจากแก้ไขในรอบงานวันที่ 2026-08-30 หากข้อความเก่าในเอกสารส่วนอื่นขัดแย้งกับส่วนนี้ ให้ยึดส่วนนี้และหัวข้อภาษาอังกฤษ `Authoritative Current State` เป็นหลัก

## ภาพรวม STANDARD กับ DEEP

| พฤติกรรม | `STANDARD` | `DEEP` |
|---|---|---|
| สิทธิ์ Premium | ไม่ต้องใช้ | ต้องมี และ API เป็นผู้ตรวจสอบ |
| Question Classifier | ใช้ | ข้ามทั้งหมด ไม่เรียกใช้งาน |
| Shared Generated-Answer Cache | ใช้เมื่อผล classification ผ่านเงื่อนไข | ข้ามทั้งหมด |
| ส่งคำถามดิบไป LLM | ไม่ส่ง เพราะไม่เรียก LLM | ส่งทุกครั้ง |
| ส่งความหมายไพ่จากระบบไป LLM | ไม่ส่ง เพราะไม่เรียก LLM | ไม่ส่ง |
| วิธีสร้างคำตอบ | Rule Engine | Full LLM ตาม model tier ที่ตั้งค่าไว้ |

แนวคิดหลักคือ:

```text
STANDARD = เร็ว สม่ำเสมอ ใช้ rule และ cache ได้

DEEP = ประสบการณ์ Premium ที่ให้ Full LLM
       อ่านคำถามจริงของผู้ใช้กับไพ่ที่เลือกโดยตรง
```

DEEP ต้องไม่ถูกลดรายละเอียดของคำถามให้เหลือเพียง intent เช่น `CAREER_CHANGE_JOB` และต้องไม่นำคำตอบที่สร้างให้คำถามของผู้ใช้อื่นมาใช้ซ้ำ

## Flow จริงของ DEEP

```text
POST /api/readings/generate
        |
        v
ตรวจสอบสิทธิ์ Premium
        |
        v
ข้าม Question Classifier
        |
        v
ข้าม Redis/PostgreSQL Generated-Answer Cache
        |
        v
ส่งคำถามดิบ + ไพ่ที่เลือก ไปยัง Full LLM
        |
        v
ตรวจ JSON schema ภาษา ลำดับไพ่ และตัวตนของไพ่
        |
        v
คืนคำตอบ DEEP โดย CacheStatus = SKIPPED
```

ทุกคำขอ DEEP จึงเรียก Full LLM ใหม่เสมอ แม้คำถาม ไพ่ และ `answerVariant` จะเหมือนคำขอก่อนหน้า

## ข้อมูลที่ส่งใน user role ของ LLM

ส่งเพียงสอง property ระดับบนสุด คือ `question` และ `cards`:

```json
{
  "question": "ฉันควรเปลี่ยนงานหรือไม่?",
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

ในแต่ละใบส่งเพียง:

```text
position
cardId
orientation
```

ห้ามเพิ่มข้อมูลต่อไปนี้กลับเข้าไปใน user role ของ DEEP:

```text
classification
domain / intent
rule interpretation payload
ชื่อไพ่ (cardName)
ความหมายไพ่หรือบทสรุปความหมาย
keywords ของความหมาย
domain meaning
mainTheme / opportunity / challenge / guidance ที่ระบบสร้างไว้ก่อน
```

คำสั่งเรื่องภาษา ความปลอดภัย รูปแบบ JSON และ response schema ยังคงอยู่ใน system/API configuration ตามปกติ Backend ยังสามารถใช้ payload ภายในสำหรับกำหนด schema ตรวจจำนวนไพ่ ตรวจลำดับไพ่ parse คำตอบ และวัดคุณภาพได้ แต่ห้าม serialize payload นั้นเข้าไปใน user-role prompt

## ทำไมไม่ส่งความหมายไพ่ให้ DEEP

รอบแรกของการแก้ไขได้ส่ง:

```text
คำถามดิบ
+ ไพ่ที่เลือก
+ ความหมายไพ่แบบ structured
```

วิธีนี้ทำงานทางเทคนิคและผ่าน test แต่คุณภาพเนื้อหายังไม่ลึกพอ เพราะความหมายไพ่ในระบบเป็นบทสรุประดับกลางที่ถูกย่อไว้แล้ว เมื่อส่งให้ Full LLM โมเดลจึงยึดข้อความเหล่านั้นเป็นกรอบ และมักทำเพียงการเรียบเรียงความหมายทั่วไป แทนที่จะวิเคราะห์ความสัมพันธ์ระหว่างคำถามจริงกับไพ่อย่างลึกซึ้ง

วิธีสุดท้ายจึงส่งเฉพาะคำถามจริงและข้อมูลการเลือกไพ่ แล้วสั่งให้ Full LLM ใช้ความรู้ Tarot ของโมเดลเอง โดยพิจารณา:

- สัญลักษณ์และแก่นความหมายของไพ่
- ไพ่ตั้งตรงหรือกลับหัว
- ตำแหน่งใน spread
- ความสัมพันธ์กับไพ่ใบอื่น
- รายละเอียดและบริบทในคำถามจริงของผู้ใช้

เป้าหมายคือให้คำอธิบายแต่ละใบเฉพาะเจาะจงกับคำถาม ไม่ใช่คำจำกัดความไพ่ทั่วไป

ข้อแลกเปลี่ยนคือคำตอบ DEEP อาจมีความหลากหลายมากขึ้นและพึ่งความรู้ภายในของโมเดลมากขึ้น จึงต้องคง JSON schema, safety prompt, language validation, card identity validation และ quality scorer ไว้เสมอ

## Metadata เมื่อข้าม Classifier

Response contract เดิมกำหนดให้มี `classification` จึงใช้ค่าที่บอกตามตรงว่าไม่ได้ classify:

```text
classification.domain         = GENERAL
classification.intent         = UNCLASSIFIED
classification.confidence     = 0
classification.source         = DEEP_DIRECT
classification.decisionMethod = CLASSIFIER_BYPASSED
cacheStatus                   = SKIPPED
cacheKey                      = null
```

ข้อมูลนี้เป็น metadata ใน response เท่านั้น ไม่ถูกส่งให้ LLM ใน user role

## สิ่งที่ทำงานสำเร็จ

- ตรวจ `ReadingMode.DEEP` ก่อนเรียก `IQuestionClassifier` ทำให้ DEEP ไม่เรียก classifier model จริง
- DEEP ข้ามทั้ง Redis cache และ persistent generated-answer store ใน PostgreSQL
- เก็บคำถามดิบไว้ทุกครั้งที่เรียก LLM ไม่มีการแทนด้วย `null`
- user-role prompt มีเพียง `question` และ `cards`
- แต่ละ card มีเพียง `position`, `cardId`, `orientation`
- Full LLM ใช้ความรู้ Tarot ของตัวเองแทนบทสรุปความหมายระดับกลางจากระบบ
- Premium access policy, model routing, worker failover, retry, timeout, concurrency gate และ cloud privacy policy ยังทำงาน
- JSON schema, parser, language validation, card-order validation และ quality scorer ยังทำงาน
- STANDARD ยังคงใช้ classifier, rule renderer, cache, persistence และ cache lock เหมือนเดิม

## สิ่งที่ลองแล้วไม่ตอบโจทย์ หรือถูกแทนที่

- การ classify คำถาม DEEP ทำให้คำถาม Premium ถูกลดเหลือ intent กว้างเกินไป จึงยกเลิก
- การตัด raw question ออกจาก LLM เมื่อ cache ได้ ทำให้คำตอบไม่ตรงบริบท จึงยกเลิก
- การ reuse คำตอบ DEEP จาก cache อาจนำคำตอบของคำถามหนึ่งไปใช้กับอีกคำถาม จึงยกเลิก
- การส่ง classification และ interpretation payload ทั้งก้อนไป LLM ทำให้ข้อสรุปจากโมเดลเล็ก/rule engine ครอบ Full LLM จึงยกเลิก
- การส่ง card meaning แบบย่อผ่าน test แต่คำตอบยังเป็นระดับกลางและทั่วไป จึงนำออก
- การรัน `dotnet test` แบบปกติครั้งแรกไม่สำเร็จ เพราะ API ที่กำลังทำงาน lock ไฟล์ `apps/api/bin/Debug/net9.0/TarotDestiny.Api.dll` นี่เป็นปัญหาไฟล์ output ถูกใช้งาน ไม่ใช่ code failure เมื่อตั้ง `BaseOutputPath` แยกแล้ว test ผ่าน

## ผลการตรวจสอบ

```text
Passed: 71
Failed: 0
Skipped: 0
```

Test ครอบคลุมว่า:

- DEEP ไม่เรียก classifier
- DEEP ทุก request เรียก LLM และส่ง raw question
- DEEP ไม่อ่าน เขียน หรือ reuse generated-answer cache
- DEEP คืน `CacheStatus.SKIPPED` และไม่มี cache key
- user content มีเฉพาะ `question` กับ `cards`
- card ไม่มี `cardName`, `meaning`, `keywords`, classification หรือ payload
- คำขอ DEEP ที่ซ้ำหรือมาพร้อมกันไม่ถูกรวมเป็นคำตอบ cache เดียว
- พฤติกรรม cache และ persistence ของ STANDARD ยังผ่าน test

## กฎสำหรับการแก้ไขในอนาคต

1. ห้ามเพิ่ม classifier กลับเข้า flow ของ DEEP
2. ห้าม cache หรือ persist shared generated answer ของ DEEP
3. ห้ามลบหรือแทน raw question ก่อนเรียก Full LLM
4. ห้ามส่ง card meaning, card name, classification หรือ rule payload ใน user role
5. ต้องรักษา card position, id, orientation และลำดับเดิม
6. ต้องตรวจ premium entitlement ที่ backend เสมอ
7. ต้องคง structured output validation และ safety rules
8. หากต้องการเพิ่ม context ให้ประเมินก่อนว่าจะกลายเป็นกรอบที่ลดความลึกของ Full LLM หรือไม่

---

# 0. Authoritative Current State — DEEP Reading Update (2026-08-30)

This section records the completed `/api/readings/generate` work from the current implementation session. It supersedes older recommendations elsewhere in this document wherever they conflict with the behavior below.

## Final Mode Boundary

| Behavior | `STANDARD` | `DEEP` |
|---|---|---|
| Premium entitlement | Not required | Required and enforced by the API |
| Question classifier | Used | Bypassed completely |
| Shared generated-answer cache | Used when classification is eligible | Always skipped |
| Raw question sent to LLM | No LLM call | Always |
| Card master meanings sent to LLM | No LLM call | Never |
| Generation | Rule renderer | Full configured LLM |

The purpose of `DEEP` is now a genuinely question-specific premium reading. It must not be reduced to a reusable classified intent or a cached response created for another user's wording.

## Final DEEP Request Flow

```text
POST /api/readings/generate
        |
        v
Verify premium entitlement
        |
        v
Bypass question classifier
        |
        v
Skip Redis/PostgreSQL generated-answer cache
        |
        v
Send raw question + selected cards to full LLM
        |
        v
Validate structured JSON and card identity/order
        |
        v
Return DEEP response with CacheStatus.SKIPPED
```

The response uses explicit metadata to show that classification was not performed:

```text
classification.source         = DEEP_DIRECT
classification.decisionMethod = CLASSIFIER_BYPASSED
classification.intent         = UNCLASSIFIED
cacheStatus                   = SKIPPED
cacheKey                      = null
```

This internal marker preserves the existing non-null API response contract without pretending that a classifier model produced a result. It is not included in the LLM user message.

## Exact DEEP User-Role Content

The LLM user-role message contains exactly two top-level properties:

```json
{
  "question": "Should I change my job?",
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

Do not add any of the following to the DEEP user-role message:

```text
classification
intent/domain
rule interpretation payload
card name
card meaning or summary
meaning keywords
domain meaning
pre-generated theme, opportunity, challenge, or guidance
```

Locale instructions, safety rules, JSON-only requirements, and the response schema remain in the system/API request configuration. Card count and identity are still constrained by the structured response schema and validated after generation.

## Why Card Meanings Are Intentionally Excluded From DEEP

The first implementation in this session sent the raw question plus selected cards and structured card meanings. It was technically valid and passed tests, but the content quality was not deep enough. The supplied meanings were already condensed, medium-depth summaries, so they anchored the full model to generic synthesis instead of letting it reason deeply about the exact user question.

The final implementation sends only the question and card selection. The full LLM is instructed to use its own established Tarot knowledge, orientation, spread position, surrounding-card relationships, and the user's precise context. Each card interpretation must be detailed and question-specific rather than a generic definition.

This is an intentional exception to the general rule-engine architecture used by `STANDARD` mode:

```text
STANDARD: application master meanings decide the interpretation.
DEEP: the full LLM interprets the selected cards directly against the raw question.
```

## What Worked

- Checking `ReadingMode.DEEP` before invoking `IQuestionClassifier` successfully removes the classifier-model call.
- Skipping shared and persistent generated-answer caches guarantees that every DEEP request reaches the full LLM with its own raw question.
- Sending only `question` and `cards` keeps classifier and rule-engine output out of the user prompt.
- Restricting every card object to `position`, `cardId`, and `orientation` removes prewritten meaning anchors.
- The existing JSON schema, parser, response validator, language validation, quality scorer, retry/failover routing, concurrency gate, and premium access policy remain active.
- `STANDARD` classification, rule rendering, caching, persistence, and cache locking remain unchanged.

## What Did Not Work or Was Replaced

- Classifying DEEP questions before generation was replaced because premium users should receive the full-model experience, not an intent-reduced reading.
- Removing the raw question on cache-eligible DEEP requests was replaced because it made the reading generic.
- Reusing cached DEEP responses was replaced because a response for one classified intent could ignore the current user's exact wording and context.
- Sending the complete interpretation payload was rejected because it exposed classifier/rule-engine conclusions to the LLM.
- Sending condensed card meanings was tried and then removed because the output remained medium-depth rather than deeply tied to the user question.
- The first normal `dotnet test` verification attempt could not overwrite `apps/api/bin/Debug/net9.0/TarotDestiny.Api.dll` because a running API process had locked that file. This was an environment/build-output lock, not a code failure. Running the same suite with an isolated `BaseOutputPath` succeeded.

## Files Updated In This Session

```text
apps/api/Contracts/ReadingContracts.cs
apps/api/Services/TarotReadingService.cs
apps/api/Services/LlmClient.cs
tests/TarotDestiny.Api.Tests/LlmClientTests.cs
tests/TarotDestiny.Api.Tests/ReadingGenerationTests.cs
tests/TarotDestiny.Api.Tests/AdvancedCacheAndInferenceTests.cs
tests/TarotDestiny.Api.Tests/GeneratedAnswerPersistenceTests.cs
tests/TarotDestiny.Api.Tests/TestSupport.cs
SUMMARY.md
```

`docker-compose.yml` already had a separate working-tree modification and was not changed as part of this request.

## Verification

The final automated result is:

```text
Passed: 71
Failed: 0
Skipped: 0
```

Regression coverage verifies that:

- DEEP does not invoke classifier methods.
- Every DEEP request calls the LLM and retains its raw question.
- DEEP returns `CacheStatus.SKIPPED` with no cache key.
- The LLM user content has only `question` and `cards` at the top level.
- Each card has only `position`, `cardId`, and `orientation`.
- No classification, full payload, card name, or card meaning is present.
- Concurrent and repeated DEEP requests are not collapsed into shared cached responses.
- STANDARD cache and persistence behavior continues to pass.

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

For Standard readings, do not rely on an LLM to invent Tarot meanings from scratch.

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

This knowledge base becomes the application's source of truth for Standard.

Deep is the deliberate exception: it does not receive these stored meanings and uses the full model's own Tarot knowledge against the exact raw question.

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

The selected generator should synthesize this into one coherent reading: the rule engine for Standard, or the full LLM for Deep.

---

# 8. Rule Engine vs LLM

The generation boundary is mode-specific:

```text
STANDARD
= Rule engine decides and renders the reading from Tarot master data
= No LLM call

DEEP
= Full LLM interprets the raw question and selected cards
= No classifier result, rule interpretation, or card meaning in the user prompt
```

For Standard, the backend prepares structured interpretation information from application master data. This provides consistent, inexpensive, cacheable readings.

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

The Standard rule renderer converts this into the structured response without an LLM.

Benefits:

- Lower token usage
- Faster inference
- Better consistency
- Easier caching
- Easier prompt versioning
- Less hallucination

Deep intentionally chooses a different quality tradeoff. The backend supplies card identity, position, and orientation, while the full LLM applies its own Tarot knowledge directly to the exact user question. Response-schema enforcement and validation still constrain the output.

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

Standard avoids LLM calls entirely. Deep is an intentional premium full-model call, so optimize its routing and concurrency without bypassing it through classification or generated-answer reuse.

---

## Standard And Premium Deep Reading Modes

The user chooses the generation mode before revealing the reading:

```text
STANDARD
→ Rule-engine reading
→ Question classification and eligible finished-answer caching
→ No LLM call
→ Available without a premium entitlement

DEEP
→ Premium option
→ Bypasses question classification and generated-answer caching
→ Sends only the raw question and selected card position/id/orientation
→ Uses the full configured LLM's Tarot knowledge directly
→ Requires a backend-verified entitlement
```

The browser must never be trusted to declare that a user has paid. In production, authentication or billing infrastructure grants the server-side claim `tarot:deep_reading=true`; the ASP.NET backend enforces that claim before any Deep generation. Development may enable an explicit local bypass for testing.

Both modes return the same structured reading contract. Only Standard uses the shared finished-answer cache. Deep always returns `CacheStatus.SKIPPED` and `CacheKey = null`, ensuring each premium question reaches the full LLM.

If Deep inference returns malformed JSON, changes card identity/order, or uses the wrong output language, validation rejects it and the response is not cached.

---

# 10. STANDARD Finished-Answer Cache

The main optimization idea is:

```text
If a new user receives the same effective question type
and the same Tarot spread/cards,
reuse a previously generated finished Standard answer.
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
Return  Rule renderer
cache    ↓
       Store response
          ↓
       Return
```

The cache stores the **finished structured Standard response**. Deep does not enter this flow and never reads or writes generated-answer cache entries.

---

# 11. Why Classify STANDARD Questions First

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

Then Standard users with the same question intent and same selected cards can share a finished cached answer. Deep questions bypass classification and are never shared.

---

# 12. STANDARD Question Classification Design

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

# 13. Personalized STANDARD Questions

Not every Standard question should reuse a generic cached answer. Deep never reuses a generated answer, regardless of personalization.

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
Generate directly with the Standard rule renderer
```

This prevents inappropriate reuse.

---

# 14. STANDARD Classifier Confidence

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

# 15. STANDARD Classifier Evolution

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

The shared Standard finished-answer cache key should depend on:

```text
Question domain
Question intent
Spread type
Card position
Card identity
Card orientation
Locale
Prompt version
Interpretation version
```

Example canonical input:

```text
CAREER
CAREER_CHANGE_JOB
STANDARD
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

Deep has no generated-answer cache key because every request uses the raw question and full LLM directly.

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

Prompt version remains part of Standard cache identity and deployment/configuration tracking. Deep does not use a generated-answer cache, so a Deep prompt change affects the next request immediately.

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

old cached Standard responses naturally stop matching.

This is better than manually clearing all previous cached entries.

---

# 19. Interpretation Versioning

Tarot meanings may also evolve.

Example:

```text
INTERPRETATION_V1
INTERPRETATION_V2
```

Include this version in the Standard cache key.

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
- Easy Standard caching
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

## L3 — Finished STANDARD Answer Cache

Primary Standard-mode optimization for this project.

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

can return a previously generated complete Standard reading without running the rule renderer again. Deep never uses L3.

---

# 22. Cache Hit vs Cache Miss

Standard cache HIT:

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
No LLM is involved
```

Standard cache MISS:

```text
Any cache identity input differs
```

Result:

```text
Generate with the rule renderer
Validate
Save result
Return
```

Skip Standard shared cache:

```text
Low classifier confidence
or
Highly personalized question
```

Result:

```text
Generate directly with the Standard rule renderer
```

Deep always skips this entire cache flow and calls the full LLM with the raw question.

---

# 23. Cache Growth Strategy

The cache can grow naturally from production traffic.

Example:

```text
First request
→ MISS
→ Generate with Standard rule renderer
→ Cache result

Second matching request
→ HIT
→ No LLM
```

Over time:

```text
Traffic creates a library of common Tarot readings.
```

This is especially useful for common Standard question types and popular spreads. Deep traffic does not grow or reuse this library.

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

One-card Standard readings can be pre-generated or cached aggressively. One-card Deep readings still call the full LLM.

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

This lazy-generation cache flow applies to Standard only. Deep always generates a fresh response.

---

# 26. Multiple Cached Variants

One future Standard-mode optimization is to store multiple wording variants for the same cache identity.

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
Standard rule renderer
 ↓
Save PostgreSQL
 ↓
Save Redis
 ↓
Return
```

If Redis is flushed, the system can repopulate cache from PostgreSQL.

Generated Standard answers become reusable content assets rather than disposable cache entries. Deep responses are returned directly and are not stored in this shared generated-answer library.

---

# 28. Cache Stampede Problem

If many users request the same uncached reading at the same time:

```text
100 users
→ same cache key
→ 100 misses
→ 100 duplicate Standard generations/writes
```

This wastes application and database work even though Standard does not use the GPU.

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

## STANDARD Classifier

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
Users → Cloudflare → ASP.NET Backend
                         │
                         ├── STANDARD
                         │      ↓
                         │   Classifier (Python gRPC with C# fallback)
                         │      ↓
                         │   Eligible cache? → Redis → PostgreSQL
                         │      ↓ MISS/SKIP
                         │   Rule renderer → validate → persist/cache when eligible
                         │
                         └── DEEP (premium entitlement required)
                                ↓
                             Bypass classifier and generated-answer cache
                                ↓
                             Raw question + selected cards
                                ↓
                             Tailscale → Private GPU → vLLM/Qwen/Llama
                                ↓
                             Validate structured response → return directly
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
(STANDARD only)

Cache eligible requests
Cache hits
Cache misses
Cache hit rate
(STANDARD only)

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

For Deep specifically, monitor direct-request count, entitlement rejection count, latency, retries/failover, structured-output rejection, language rejection, quality score, and confirmation that cache status remains `SKIPPED`.

---

# 38. Expected Scaling Behavior

For Standard at low traffic:

```text
Many MISS
→ Cache gradually grows
```

For Standard at higher traffic:

```text
Common question intents repeat
Common card combinations repeat
→ More HITs
→ Less rule/database work per user
```

The system should gradually build a reusable library of common Standard Tarot readings. Deep GPU demand scales with the number of premium Deep requests because those responses are intentionally neither classified nor shared.

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
1. Tarot master data is the source of truth for STANDARD readings.

2. STANDARD uses the rule engine and no LLM.
   DEEP lets the full LLM interpret the raw question and selected cards.

3. DEEP is a premium full-model call.
   Do not reduce it through classification or shared answer reuse.

4. Classify question intent before STANDARD cache lookup only.

5. Reuse eligible STANDARD readings when the effective intent
   and card spread are the same. Never share DEEP generated answers.

6. Always send the exact raw question for DEEP.

7. Card position and orientation matter.

8. The DEEP user prompt contains only question and card selection.
   Do not send card names, meanings, classifier data, or rule payloads.

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
