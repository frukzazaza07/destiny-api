# Queued cloud DEEP readings

Implemented 2026-09-08. Enable with `docker-compose.jobs.yml` after applying the EF migration. The base deployment keeps its previous behavior until this overlay is selected. No real cloud account is required for the integration tests.

## Ownership and execution

```
Browser -> C# job API -> PostgreSQL job + credit reservation + outbox transaction
                        -> confirmed RabbitMQ REQUEST
                        -> NestJS worker -> authenticated C# claim -> cloud provider
                        -> confirmed RabbitMQ RESULT/FAILURE
                        -> C# schema/card/language validation + completion/credit transaction
Browser <- C# SSE status snapshots, including the validated persisted reading
```

C# owns request validation, identity, entitlements, credits, prompt instructions, authoritative cards, admission limits, lifecycle, and output validation. The NestJS/Fastify service owns HTTP provider transport, cancellation, and confirmed result delivery. The browser never contacts the worker, broker, or provider.

STANDARD still uses the existing C# rule renderer and caches. With queued mode enabled, synchronous DEEP generation returns `409 USE_READING_JOBS`; synchronous admin/warmup callers cannot invoke an LLM either. Queued DEEP uses the existing direct-DEEP prompt and authoritative validation, with strict output-schema validation added. It deliberately does not put personal readings into the shared answer cache. Completed jobs are private to their owner.

The existing `LLM__AllowCloudForRequestsWithRawQuestion` privacy switch must be explicitly enabled before questions can reach the cloud. It is checked both at submission and immediately before execution. Provider reference and model must match the worker configuration. Keep a reference immutable for the lifetime of accepted jobs; drain before changing its model. Provider keys exist only in worker configuration, never in messages, jobs, or logs. Raw request JSON is cleared on every terminal transition; the request hash remains for idempotency. Completed reading content remains available through the owner-scoped status endpoint. Treat the job database, result queue, dead-letter queue, and their backups as private application data; apply the deployment's retention/access policy.

## Durable state and credit policy

States are `QUEUED`, `RUNNING`, `COMPLETED`, `FAILED`, and `CANCELED`. PostgreSQL transactions take advisory lock `827194302` for short lifecycle/admission decisions across API replicas. No provider or broker call runs inside this lock. Every terminal transition updates the job and reserved credit in the same transaction. This intentionally favors simple, auditable correctness at the initial bounded queue size.

| Outcome | Credit |
| --- | --- |
| Accepted job | Reserve one credit unless premium access already authorizes it |
| Successful validated completion | Consume the reservation exactly once |
| Queued or running cancellation | Release the reservation |
| Provider failure, invalid output, deadline, or execution lease loss | Release the reservation |
| Duplicate submission/result or stale attempt | No additional reservation or settlement |
| Completion wins a cancellation race | Keep the completed result and consumed credit |
| Cancellation wins a completion race | Reject the late result and keep the released credit |

A durable reservation cannot expire into reuse while an unfinished job still owns it. Restart-safe expiry processing releases it on terminal failure/cancellation. Releasing a user credit does not refund a provider charge.

Submission keys are unique per owner. Reusing a key with the same input returns the existing job; different input returns `409`. Owner identity is the authenticated account GUID or SHA-256 of the existing high-entropy reward-session cookie. A job GUID alone grants no access. Unknown and foreign jobs both return `404`. Mutating browser endpoints require antiforgery validation, including anonymous reward sessions.

## Contracts and HTTP

Versioned JSON schemas are in `contracts/reading-jobs/v1`. `work.schema.json` covers `REQUEST` and `CANCEL`; `result.schema.json` covers `RESULT` and `FAILURE`. Runtime validation additionally requires correlation ID = job ID and disjoint result/failure fields. Export them with `node apps/worker/scripts/export-contracts.cjs` after building the worker.

Messages include version, kind, job ID, attempt ID, correlation ID, absolute deadline, and provider configuration reference. Request/cancel messages contain no question or prompt. An authenticated successful claim returns the C#-built provider request and expected model. Results are size bounded and validated in C# against the same schema supplied to the provider. Unknown fields, changed/reordered cards, invalid language, missing/oversized fields, obsolete attempts, and terminal jobs cannot complete.

| Endpoint | Contract |
| --- | --- |
| `POST /api/reading-jobs` | `{idempotencyKey, reading}`; DEEP/CLOUD only; `202` with `ResponseDto<ReadingJobDto, object>` and Location |
| `GET /api/reading-jobs/{id}` | Owner-scoped persisted snapshot/result; no-store |
| `GET /api/reading-jobs/{id}/events` | SSE `status` events, each containing the same ResponseDto JSON envelope |
| `POST /api/reading-jobs/{id}/cancel` | Immediate idempotent cancellation; returns current snapshot |
| `POST /api/reading-jobs/{id}/heartbeat` | CSRF-protected `{subscriberId}` acknowledgement; renews only an existing owner-scoped subscriber |
| `POST /internal/reading-jobs/{id}/claim` | Worker key + `{attemptId}`; EXECUTE / WAIT / SKIP |
| `POST /internal/reading-jobs/{id}/renew` | Worker key + `{attemptId}`; RENEWED / SKIP |
| `GET /internal/reading-jobs/metrics` | Worker-key-protected aggregate operational snapshot |
| Worker `/health`, `/ready`, `/metrics` | Explicit envelope contracts on a private Fastify listener |

SSE IDs are increasing persisted job revisions. The initial `presence` event carries `ResponseDto<ReadingPresenceDto, object>` with a subscriber ID and heartbeat cadence. Each connection immediately reconciles the latest status snapshot, regardless of Last-Event-ID; replay of every intermediate transition is unnecessary. Periodic database reconciliation delivers updates from any API replica and recovers missed notifications without another generation. Error responses before streaming are JSON envelopes, not SSE frames. Errors and progress use the existing Thai/English reading UI.

Swagger UI and generated JSON are available in Development at `/swagger` and `/swagger/v1/swagger.json` on each service. They are absent in Production. The API OpenAPI description explains SSE payloads, reconnect semantics, and cancellation. Nginx blocks `/internal/`, disables SSE buffering, and keeps a 60-second upstream idle timeout, above the keepalive cadence.

## Presence, cancellation, and recovery timing

Defaults:

- First SSE subscription must arrive within 15 seconds of acceptance.
- Each subscriber has its own persisted 10-second presence lease, renewed by browser POST heartbeats every 5 seconds while streaming. Server keepalives use the same cadence but never renew presence themselves. An open TCP stream without browser acknowledgements therefore still expires.
- A clean last disconnect starts the 15-second reconnect grace immediately. A process/network disappearance is detected after the remaining presence lease, followed by 15 seconds: worst case about 25 seconds plus the 1-second expiry scan and database availability.
- Another active subscriber keeps the job alive. Reconnecting within grace renews presence; reconnecting after expiry cannot revive a canceled job. Hiding a tab has no special cancellation behavior.
- The execution lease is 15 seconds; workers renew every 4 seconds with a 3-second control-request timeout. Any failed renewal aborts transport. Cancellation fanout normally aborts sooner; persisted state/lease checks remain authoritative if a signal is lost.
- End-to-end job deadline is 180 seconds by default, configurable up to 240 seconds. Request/result queue TTL is 300 seconds.

Closing the browser relies on connection/presence expiry, not unload delivery. Explicit Cancel or Restart requests cancellation immediately. The frontend waits for cancellation to succeed before submitting replacement work. Internal 2D/3D transitions retain the shared hook and active job. Browser retry stores only an opaque submission key under an input fingerprint in session storage; it can reconcile an existing job instead of creating another one. A deliberate reset clears that key. Completed results remain retrievable through the API after departure.

RabbitMQ uses durable queues, persistent messages, publisher confirms, manual acknowledgements, bounded worker prefetch, and a dead-letter queue. Publish-before-outbox-mark and commit-before-result-ack crashes can produce duplicates; idempotency and terminal/attempt fencing make those harmless. Request/cancellation outbox publishing and expiry are separate hosted services, so broker downtime does not stop presence expiry.

Result-delivery retries never re-enter provider generation. Explicit provider `429` failures are retried with a new fenced attempt, up to three total attempts, under a shared 30-second cooldown plus randomized claim polling. Other ambiguous provider failures are not automatically regenerated. Result publishing makes up to six confirmation attempts with bounded exponential delay and jitter; connection loss requeues unacknowledged deliveries. Poison/schema-invalid messages are dead-lettered; expired/stale jobs are acknowledged and skipped.

If a worker crashes after the provider may have completed but before confirming its result, the execution lease expires and C# fails the job and releases its credit. The first version chooses failure over a possible second provider charge. If the result was confirmed before a crash, another API consumer can commit it while the lease/deadline remains valid. A sufficiently long API outage can still invalidate that lease and discard the result. There is no generic exactly-once provider charging guarantee. When the chosen provider documents support, set `PROVIDER_IDEMPOTENCY_HEADER` to `Idempotency-Key` or `X-Idempotency-Key`; its value is the stable attempt ID. It defaults to disabled and does not change the conservative crash policy. Aborting HTTP does not guarantee provider computation or charges stop.

## Configuration and deployment

The overlay requires externally supplied `RABBITMQ_PASSWORD`, `READING_WORKER_KEY` (at least 32 characters), `TAROT_LLM_CLOUD_ENDPOINT`, `TAROT_LLM_CLOUD_API_KEY`, and `TAROT_LLM_CLOUD_MODEL`. Set `ALLOW_CLOUD_RAW_QUESTION=true` only for the approved cloud deployment. Do not inspect or copy secret-bearing environment files. These are configuration keys, not example credentials for production.

`infra/reading-jobs.env.example` contains nonproduction examples for configuration validation. It keeps cloud-question forwarding disabled.

```sh
docker compose -f docker-compose.yml -f docker-compose.jobs.yml build api worker
docker compose -f docker-compose.yml -f docker-compose.jobs.yml run --rm migrate
docker compose -f docker-compose.yml -f docker-compose.jobs.yml up -d --build
docker compose -f docker-compose.yml -f docker-compose.jobs.yml up -d --scale worker=2 worker
```

RabbitMQ has a persistent volume and no host port. Workers have no host port; they join private backend and outbound-provider networks. Existing shared API data-protection storage is required across replicas for account/CSRF cookies. TLS terminates at the existing trusted edge. Worker shutdown stops admission, aborts active transport, allows up to 10 seconds for result delivery, and requeues work it has not claimed. Docker allows 20 seconds to stop it.

C# `ReadingJobs__*` keys: `Enabled`, `BrokerUri`, `WorkerKey`, `ProviderRef`, `Model`, `DeadlineSeconds`, `ReconnectGraceSeconds`, `HeartbeatSeconds`, `PresenceLeaseSeconds`, `ExecutionLeaseSeconds`, `MaxConcurrency`, `RequestsPerMinute`, `TokensPerMinute`, `MaxQueuedJobs`, `MaxAttempts`, and `MaxOutputTokens`. The cloud-question switch is inherited from `LLM__AllowCloudForRequestsWithRawQuestion`.

Worker keys: `BROKER_URI`, `API_URL`, `WORKER_KEY`, `PROVIDER_REF`, `PROVIDER_URL`, `PROVIDER_KEY`, `PROVIDER_MODEL`, `PROVIDER_IDEMPOTENCY_HEADER`, `PREFETCH`, `PORT`, `NODE_ENV`. Defaults are reference `cloud-default`, prefetch 4, and port 3100. Provider URLs require HTTPS except loopback test providers. No dotenv loader is used. Startup configuration errors do not print supplied values.

## Limits and operations

The initial deployment uses one conservative shared provider/account budget for all workers: four concurrent executions, 20 admissions/minute, 200,000 reserved tokens/minute, and at most 100 unfinished jobs. At most one unfinished job per owner is admitted. Every provider attempt consumes a shared request reservation. Input UTF-8 bytes plus maximum output tokens provide a conservative token reservation; reservations are not refunded on cancellation or rate limiting. Configure limits below the provider's actual account limits, including any usage outside this application. Replicas must share identical quota configuration and PostgreSQL.

Scale using oldest queued job age and queue depth alongside active execution count and remaining request/token reservations. Increase replicas only when work is waiting and shared quota has headroom. Desired worker slots cannot exceed the shared concurrency cap; estimate additional slots from provider latency and remaining rate budget, then divide by worker prefetch. Scaling replicas does not increase allowed admissions. This repository supplies Compose scaling and metrics; it does not assume an external autoscaler installation.

The internal API metrics expose queued/running jobs, oldest queued age, recent admissions/reserved tokens, pending outbox count, and failed/canceled counts. Worker metrics expose active requests, calls, cancellations, and delivery retries. Structured worker logs include job/attempt IDs, latency, cancellation latency, outcome, and numeric token/cost usage when the provider supplies it. C# logs result disposition and broker queue/dead-letter depth. Questions, readings, provider credentials, and account details are excluded. Alert on growing outbox/queue age, dead letters, rate-limit outcomes, lease loss, and late-result discards. Do not replay dead-letter messages as new jobs automatically.

## Verification

Use isolated test containers with the following explicitly nonproduction credentials:

```sh
docker run -d --name destiny-jobs-test-db -p 127.0.0.1:55439:5432 -e POSTGRES_USER=tarot_test -e POSTGRES_PASSWORD=example-only -e POSTGRES_DB=tarot_jobs_test postgres:17-alpine
docker run -d --name destiny-jobs-test-broker -p 127.0.0.1:56739:5672 -e RABBITMQ_DEFAULT_USER=tarot_test -e RABBITMQ_DEFAULT_PASS=example-only rabbitmq:4.1-alpine
dotnet build apps/api
cd apps/worker
npm ci
npm test
cd ../..
node scripts/test-reading-jobs.cjs
```

For the PostgreSQL lifecycle suite in PowerShell:

```powershell
$env:TAROT_JOBS_INTEGRATION='1'
dotnet test
```

The lifecycle tests create and drop uniquely named `destiny_jobs_test_*` databases only on the isolated test server. They exercise migrations, reservation/settlement, duplicate and concurrent submission, ownership, cancellation/completion races, initial subscription timeout, multiple subscribers, reconnect, stale attempts, deadline, shared admission, privacy policy, strict schema rejection, and request cleanup.

The Node harness starts two API replicas, two real NestJS workers, and a local mock cloud provider. It checks confirmed request/result interoperability, cross-replica SSE, zero-call queued cancellation, in-flight abort, shared concurrency, broker restart/outbox recovery, real Chrome close behavior, worker/API restart recovery, and Development/Production documentation rules. It never calls a paid provider. The harness needs Docker access and installed Chrome, and stops its spawned processes. Its test containers remain available for reuse.

Frontend checks: `npm run build` and `npm run test:smoke` in `apps/web`. The existing 42 browser regressions cover public/account/reward/2D/3D flows; queued tests add localized SSE completion and shared view state. The worker suite checks strict contracts, configuration, canceled queue skipping, delivery retries without regeneration, and provider abort on lease loss.

Verification completed 2026-09-08: **126 API tests**, **6 worker tests**, and **46 Playwright browser tests** passed. The frontend production build/content/3D budgets and worker Docker build passed. The real RabbitMQ/PostgreSQL harness passed all listed recovery checks, including dead-letter handling and open-stream/no-heartbeat expiry. Worker dependency audit reported zero vulnerabilities after pinning Fastify 5.12.1. No paid cloud calls or production deployment were performed.

Transport implementation references: [RabbitMQ .NET client guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide), [publisher confirms](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet), and [NestJS OpenAPI](https://docs.nestjs.com/openapi).
