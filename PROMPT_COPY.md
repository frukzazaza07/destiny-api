# Copy a reading prompt

The API and shared Thai/English reading control implement per-reading prompt snapshots, authenticated unlock sessions, independent ad progress, and repeatable clipboard export. Provider integration is **pending**: no compatible server-verifying rewarded-ad provider has been selected. The feature defaults to unavailable for ad unlocking.

## Provider integration required

The existing Google Publisher Tag implementation reports `rewardedSlotGranted` in browser JavaScript. The existing DEEP grant endpoint accepts that browser event. Copying prompts never uses that endpoint as proof: a caller could reproduce that request without watching an ad. Google's [rewarded-ad sample](https://developers.google.com/publisher-tag/samples/display-rewarded-ad) documents the browser events used by the existing integration.

Before enabling live ads, implement the selected provider's browser adapter and server verification. The adapter is `window.tarotPromptRewardedProvider(attempt, abortSignal)`, returning `completed`, `closed`, `no-fill`, or `error`. It must bind the API-issued `attemptId` to the provider's verification metadata, display an ad only after the voluntary action and existing advertising consent check, and close its UI when canceled. Do not install a test adapter in production. Review the consent-provider wording for the selected provider before enabling it; the current wording names Google.

The provider backend must validate genuine provider completion evidence before calling `POST /api/rewards/prompt/provider-completions`. Merely forwarding or signing a browser event is not verification. The callback body is:

```json
{
  "attemptId": "11111111-1111-1111-1111-111111111111",
  "eventId": "provider-unique-completion-id",
  "timestamp": 1788912000
}
```

`timestamp` is the callback signing time in Unix seconds. The `X-Prompt-Signature` header contains hex HMAC-SHA256 using the server-only callback key over this exact UTF-8 text, with LF separators and no trailing newline:

```text
prompt-copy-v1
<attemptId in lowercase UUID D format>
<eventId>
<timestamp>
```

Sign each delivery with the current timestamp; requests outside a five-minute window are rejected. Keep `eventId` stable across retries and globally unique for each provider completion. Duplicate successful deliveries return the existing progress. Reused events, closed/expired attempts, incorrect signatures, and unfinished readings cannot grant progress. Attempts expire after ten minutes. The browser polls status; its own `completed` result grants nothing.

Server configuration keys:

| Key | Default | Meaning |
| --- | --- | --- |
| `PromptCopy:RequiredAds` | `2` | Integer 1–100; saved when each unlock session starts. |
| `PromptCopy:Enabled` | `false` | Enable only after provider verification and the browser adapter are implemented. |
| `PromptCopy:CallbackSigningKey` | empty | At least 32 UTF-8 bytes; supply through deployment secret configuration, never browser code. |

Environment equivalents use double underscores, e.g. `PromptCopy__RequiredAds`. No production secret values are included in this repository.

## Persistence and prompt behavior

Apply EF migration `20260909024504_PromptCopyUnlocks` before deploying the changed API. It creates `prompt_readings` and `prompt_ad_attempts`. PostgreSQL advisory transaction locks serialize unlock mutations across API replicas; unique provider event IDs provide an additional replay constraint. DEEP credit tables are never mutated by the prompt unlock service.

Each completed synchronous reading receives a fresh `promptReadingId`, including STANDARD and shared-cache hits. Snapshots are encrypted using ASP.NET Data Protection. Persist and share the application's Data Protection keys across replicas and deployments, otherwise existing snapshots cannot be decrypted. Anonymous readings are bound to a secure HTTP-only browser cookie and can be claimed once after login; authenticated readings belong to their account. Anonymous queued DEEP readings can also be claimed using their existing reward cookie. A new reading gets a new unlock even when its shared answer was cached.

Queued DEEP jobs snapshot their production messages when created and send those same messages at execution. Completion makes the snapshot eligible for unlocking. Failed/canceled jobs do not. Synchronous LLM results carry their actual messages internally; these are excluded from public JSON and shared persistent cache serialization. STANDARD and cache-hit requests assemble equivalent messages without an additional LLM call or credit reservation. Legacy readings without a saved prompt ID cannot export a historical configuration that was never captured.

`ReadingPromptBuilder` is shared by synchronous generation, queued generation, and equivalent prompt creation. User messages now explicitly include locale, spread, authoritative cards, and the interpretation payload in addition to the original question/topic. This increases provider input size; the normal structured response contract remains intact. Admin prompt variants retain their configured instructions and version in the saved snapshot.

Export labels the ordered system/user sections and adapts response formatting to plain text. It replaces the application's JSON-only block, field-format directions, and validation checks; removes fenced/unfenced JSON output examples and recognized extra structured-output directives. Reading guidance and safety text remain. Arbitrary administrator prose is not a formal instruction language: review export behavior when introducing unusual formatting instructions, especially mixed or multilingual directives.

The reading interface stores the completed reading in per-tab session storage for refresh/login recovery. The exported prompt itself is not stored in browser storage. Copying performs an authenticated, CSRF-protected request and supplies selectable text if clipboard access fails. No request sends the exported prompt to another AI automatically.

## Verification

API tests cover login/ownership, requirement snapshots, exactly-n completions, replay rejection, closed/expired attempts, disabled provider behavior, shared prompt contents, response envelopes, and Development Swagger UI/OpenAPI schemas. Production Swagger UI and JSON are tested as unavailable. Browser tests use a simulated provider to check both locales, both reading interfaces, consent, login recovery, refresh, repeat copying, clipboard success/fallback, and unsuccessful ad outcomes. These simulations are not evidence of live provider verification.

PostgreSQL integration and live provider validation require their corresponding external services. The local Docker engine was unavailable during implementation, so database integration tests could not be run in this session.

Session results: 117 API tests passed, 17 PostgreSQL tests skipped; 16 browser scenarios passed; frontend production build and TypeScript checks passed; EF reports no pending model changes. Swagger UI/OpenAPI Development and Production exposure checks passed as part of the API suite.
