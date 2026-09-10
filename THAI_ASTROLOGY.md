# Thai astrology consultation

Implemented 2026-09-10 in the existing C# / RabbitMQ / NestJS / Next.js architecture. No separate worker, provider credentials, or shared answer cache is introduced.

## Entry and contract

Choose **Meet the astrology advisor** on `/en` or its Thai equivalent on `/th`. Direct accessible links are `/en#astrology-consultation` and `/th#astrology-consultation`. In the shop, select the indigo advisor in the service gallery by pointer, approach and press E/Enter, or use the localized service button. Form and job state live above the views; leaving the shop or losing WebGL preserves them. Leaving the page closes presence and uses the existing cancellation grace period.

`POST /api/reading-jobs/thai-astrology` accepts the usual wrapper:

```json
{
  "idempotencyKey": "a-new-opaque-key-at-least-16-characters",
  "reading": {
    "readingType": "THAI_ASTROLOGY",
    "birthDate": "1995-04-13",
    "birthTime": null,
    "birthPlace": null,
    "question": "Should I change jobs?",
    "locale": "en"
  }
}
```

Birthdate is a real Gregorian date, never a timestamp; future dates are rejected against the API's UTC date. The UI explicitly labels Gregorian/ค.ศ.; there is no separate Buddhist Era text-entry mode or guessed year conversion. Time is optional local `HH:mm`; midnight `00:00` is preserved. Place is optional customer text, maximum 200 characters. Question is required, nonblank, maximum 2000 characters. Optional blank fields normalize to null. Locale is `th` or `en`; explicit unsupported/empty locales fail. When omitted/null, dominant Thai/Latin script resolves the language; ties and other scripts use the application default, Thai. This is script-based fallback, not a general language classifier.

Creation returns HTTP 202 in `ResponseDto<ReadingJobDto, object>`. Status, SSE, heartbeat and cancellation reuse `/api/reading-jobs/{jobId}` and its existing suffixes. Responses add `readingType` and nullable `astrologyReading`; Tarot's existing `reading` stays compatible. Astrology completion puts `reading=null` and returns `astrologyReading` with resolved locale, supplied birth fields, versions and `sections`: `overview`, `analysis`, `directAnswer`, `timing`, `timingExplanation`, `advice`, `dataLimitations`. Titles are localized by the frontend. In this version timing is always null with a required explanation. Other section strings must be nonblank and at most 8000 characters; duplicate, missing and extra properties fail validation.

## Prompt, calculation and privacy policy

The full supplied reference prompt is embedded in `apps/api/Prompts/thai-astrology-v1.txt`. C# adds trusted language, JSON schema, safety and calculation constraints. Customer fields are serialized in a separate user message. Versions are `THAI_ASTROLOGY_V1`, `ASTROLOGY_SECTIONS_V1`, and `NO_CHART_V1`.

**No chart facts are calculated in this release.** There is no ephemeris, geocoder, historical timezone source, ascendant, planetary position, house or transit computation. Supplying time and place does not enable precise calculations. The reading is explicitly limited to question-specific reflection, with unavailable-chart and missing-input disclosures. Prompts prohibit invented chart support, transit dates, guaranteed outcomes and accuracy percentages. The structural validator cannot prove every natural-language statement factually correct; live provider quality review remains necessary before launch. No paid provider calls were made during implementation.

DEEP entitlement or one earned DEEP credit is required; browser flags cannot grant it. Birth data follows `ReadingJobs:AllowCloudForRequestsWithRawQuestion` alongside the question, and the flag is checked at creation and execution. Prompt snapshots and normalized input are saved atomically with the job/outbox, and snapshots are reused on worker retries. Both are cleared on terminal states. Completed results retain supplied birth context in the owner-scoped result; there is no new automatic retention timer. Existing account deletion disables the account/revokes sessions rather than purging records. Deployments must apply their existing database retention/deletion process to job results as well. No birth data, question or prompt is added to routine telemetry or broker envelopes.

## Migration and rollout

Apply `20260910072401_ThaiAstrologyJobs` using the existing migration command before deploying the new API. It adds `ReadingType` (default `TAROT` for existing rows) and nullable `PromptJson` to `reading_jobs`. No new configuration keys are required. Enable the existing queued DEEP deployment and cloud-data policy only where intended; the browser form explains the provider transfer.

RabbitMQ work/result **v1 is unchanged**: envelopes still contain identifiers only and the authenticated C# claim supplies the provider request. Existing workers already transport the new prompt/schema. Drain and replace all old API consumers before accepting astrology submissions: old API binaries do not understand astrology result dispatch. Deploy frontend after the API and migration. Do not roll back API binaries while astrology jobs are active; drain/cancel those jobs first. Existing Tarot requests omit the discriminator and continue unchanged.

Swagger remains Development-only at `/swagger/index.html` and `/swagger/v1/swagger.json`. Astrology field limits, null handling and asynchronous semantics are documented in the generated OpenAPI.

## Verification

- API: 137 passing tests, including astrology normalization, locale/prompt authority, strict output rejection, lifecycle dispatch, idempotency, ownership, cancellation, terminal cleanup, Swagger JSON/UI and Production hiding. EF reports no pending model changes.
- Worker: 7 passing tests, including exact astrology claim payload transport, no provider call after cancellation, result delivery retry without regeneration and execution-lease abort.
- Frontend: all 58 browser tests pass, including the existing Tarot, reward, account and shop regressions. Production build, TypeScript, content and GLB validation pass. The six astrology browser cases cover Thai/English date-only/full details, midnight, validation, unknown time, failure/retry/cancel, completed-result recovery, WebGL fallback and mobile touch. Screenshot review additionally caught and corrected the mobile panel stacking order; the astrology cases were rerun with a clickability assertion after that fix.
- Actual GLBs render in Chrome; the new advisor is 1660 triangles and one draw call. All assets total 1.66 MiB; scene maximum 72,896 triangles / 60 base draw calls, within unchanged budgets. No device-lab FPS claim is made.
- 19 PostgreSQL integration cases, including astrology outbox/credit completion and cancellation cases, are present but skipped here because Docker/PostgreSQL is unavailable. Real broker/multi-replica recovery and live LLM output quality were not reverified. The in-memory lifecycle tests do not establish PostgreSQL transaction/concurrency correctness.

For infrastructure verification, follow `QUEUED_DEEP.md`, start the documented isolated PostgreSQL test service, and run the API tests with `TAROT_JOBS_INTEGRATION=1`. Run the existing broker recovery harness against the configured test stack before production rollout.

## Follow-up hardening — 2026-09-10

The shared Tarot/astrology browser client now checks cancellation after asynchronous submission fingerprinting, before creating a job. Canceling at that point cannot enqueue work after the customer has canceled or started over. Closed SSE subscriptions also ignore late heartbeat/status responses, so a delayed response cannot overwrite the state of a reset consultation.

Two browser regressions cover cancellation during fingerprinting followed by a fresh submission, and a completed heartbeat arriving after starting over. All 60 browser tests pass, including existing Tarot, account, reward and 3D regressions. API verification remains 137 passing / 19 infrastructure cases skipped; all 7 worker tests and the frontend production build, TypeScript, content and asset checks pass. Docker's daemon is unavailable in this workspace, so PostgreSQL/RabbitMQ verification and live provider quality review remain pending.
