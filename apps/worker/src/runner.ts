import { Injectable, OnApplicationShutdown } from '@nestjs/common';
import amqp, { ChannelModel, ConfirmChannel, ConsumeMessage } from 'amqplib';
import { setTimeout as sleep } from 'node:timers/promises';
import { configSchema, Config, Work, workSchema, resultSchema } from './contracts';

@Injectable()
export class Runner implements OnApplicationShutdown {
  readonly config: Config;
  ready = false;
  calls = 0;
  cancellations = 0;
  deliveryRetries = 0;
  get activeRequests() { return this.active.size; }
  private stopping = false;
  private connection?: ChannelModel;
  private readonly active = new Map<string, AbortController>();
  constructor() {
    const parsed = configSchema.safeParse(process.env);
    // Do not print Zod errors: input fields may contain credentials.
    if (!parsed.success) throw new Error('Invalid worker configuration; see documented keys.');
    this.config = parsed.data;
  }
  async start() {
    while (!this.stopping) {
      try { await this.connect(); }
      catch { this.ready = false; }
      if (!this.stopping) await sleep(2000 + Math.random() * 3000);
    }
  }
  private async connect() {
    const connection = await amqp.connect(this.config.BROKER_URI);
    this.connection = connection;
    connection.on('error', () => { this.ready = false; });
    const closed = new Promise<void>(resolve => connection.once('close', () => {
      this.ready = false;
      for (const controller of this.active.values()) controller.abort();
      resolve();
    }));
    const channel = await connection.createConfirmChannel();
    channel.on('error', () => { this.ready = false; void connection.close().catch(() => {}); });
    await channel.assertQueue('tarot.dead.v1', { durable: true });
    for (const name of ['tarot.requests.v1', 'tarot.results.v1'])
      await channel.assertQueue(name, { durable: true, arguments: {
        'x-dead-letter-exchange': '', 'x-dead-letter-routing-key': 'tarot.dead.v1', 'x-message-ttl': 300000,
      }});
    await channel.assertExchange('tarot.cancellations.v1', 'fanout', { durable: true });
    const cancelQueue = await channel.assertQueue('', { exclusive: true, autoDelete: true });
    await channel.bindQueue(cancelQueue.queue, 'tarot.cancellations.v1', '');
    await channel.consume(cancelQueue.queue, msg => {
      if (!msg) return;
      try { const item = workSchema.parse(JSON.parse(msg.content.toString())); this.active.get(item.attemptId)?.abort(); }
      catch { /* Polling the authoritative lease remains the cancellation backstop. */ }
      channel.ack(msg);
    });
    await channel.prefetch(this.config.PREFETCH);
    await channel.consume('tarot.requests.v1', msg => {
      if (msg) void this.handle(channel, msg).catch(() => {
        // Closing requeues unacknowledged requests; C# fencing prevents a second claim.
        void connection.close().catch(() => {});
      });
    }, { noAck: false });
    this.ready = true;
    await closed;
  }
  private async control(work: Work, action: 'claim' | 'renew') {
    const response = await fetch(`${this.config.API_URL}/internal/reading-jobs/${work.jobId}/${action}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Worker-Key': this.config.WORKER_KEY },
      body: JSON.stringify({ attemptId: work.attemptId }), signal: AbortSignal.timeout(3000),
    });
    if (!response.ok) throw new Error('Control unavailable');
    const envelope = await response.json() as { success: boolean; data: { decision: string; leaseUntil?: string; providerRef?: string; model?: string; request?: Record<string, unknown> } };
    if (!envelope.success || !envelope.data) throw new Error('Invalid control response');
    return envelope.data;
  }
  private async handle(channel: ConfirmChannel, delivery: ConsumeMessage) {
    let work: Work;
    try {
      if (delivery.content.length > 4096) throw new Error();
      work = workSchema.parse(JSON.parse(delivery.content.toString()));
      if (work.kind !== 'REQUEST' || work.providerRef !== this.config.PROVIDER_REF) throw new Error();
    } catch { channel.nack(delivery, false, false); return; }
    // One local controller per attempt; duplicate delivery must never replace its cancellation handle.
    if (this.active.has(work.attemptId)) { channel.ack(delivery); return; }
    const controller = new AbortController();
    let abortedAt: number | undefined;
    controller.signal.addEventListener('abort', () => { abortedAt = Date.now(); }, { once: true });
    this.active.set(work.attemptId, controller);
    const remaining = Date.parse(work.deadline) - Date.now();
    const deadlineTimer = setTimeout(() => controller.abort(), Math.max(0, remaining));
    let leaseTimer: ReturnType<typeof setInterval> | undefined;
    try {
      let claim;
      while (!controller.signal.aborted && !this.stopping) {
        claim = await this.control(work, 'claim');
        if (claim.decision === 'SKIP') { channel.ack(delivery); return; }
        if (claim.decision === 'EXECUTE') break;
        await sleep(1500 + Math.random() * 2000, undefined, { signal: controller.signal });
      }
      if (this.stopping) { channel.nack(delivery, false, true); return; }
      if (controller.signal.aborted || !claim || claim.decision !== 'EXECUTE') { channel.ack(delivery); return; }
      if (!claim.request || claim.providerRef !== this.config.PROVIDER_REF || claim.model !== this.config.PROVIDER_MODEL) throw new Error('Invalid claim');
      // Any failed renewal aborts transport immediately; no provider work starts without a C# claim.
      leaseTimer = setInterval(() => {
        void this.control(work, 'renew').then(value => { if (value.decision !== 'RENEWED') controller.abort(); })
          .catch(() => controller.abort());
      }, 4000);
      const started = Date.now();
      this.calls++;
      let body: string | null = null;
      let errorCode: string | null = null;
      try {
        const response = await fetch(this.config.PROVIDER_URL, {
          method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${this.config.PROVIDER_KEY}`,
            ...(this.config.PROVIDER_IDEMPOTENCY_HEADER ? { [this.config.PROVIDER_IDEMPOTENCY_HEADER]: work.attemptId } : {}) },
          body: JSON.stringify({ ...claim.request, model: this.config.PROVIDER_MODEL }), signal: controller.signal,
        });
        if (!response.ok) {
          // Retry only explicit rate-limit rejections. Ambiguous failures can already have incurred cost.
          errorCode = response.status === 429 ? 'RATE_LIMITED' : 'PROVIDER_FAILED';
          await response.body?.cancel();
        } else {
          const reader = response.body?.getReader();
          if (!reader) throw new Error('Empty response');
          const chunks: Uint8Array[] = []; let bytes = 0;
          for (;;) {
            const chunk = await reader.read(); if (chunk.done) break;
            bytes += chunk.value.length;
            if (bytes > 262144) { await reader.cancel(); throw new Error('Response too large'); }
            chunks.push(chunk.value);
          }
          body = Buffer.concat(chunks).toString('utf8');
        }
      } catch {
        errorCode = controller.signal.aborted ? 'ABORTED' : 'PROVIDER_FAILED';
        if (controller.signal.aborted) this.cancellations++;
      }
      const result = resultSchema.parse({ ...work, kind: errorCode ? 'FAILURE' : 'RESULT', body, errorCode });
      let totalTokens: number | undefined; let reportedCost: number | undefined;
      if (body) {
        try {
          const usage = JSON.parse(body).usage;
          if (Number.isSafeInteger(usage?.total_tokens) && usage.total_tokens >= 0) totalTokens = usage.total_tokens;
          if (typeof usage?.cost === 'number' && Number.isFinite(usage.cost) && usage.cost >= 0) reportedCost = usage.cost;
        } catch { /* C# owns validation; telemetry never exposes provider content. */ }
      }
      // Result publishing retries never re-enter the provider call. Keep lease renewal during delivery.
      for (let deliveryAttempt = 0; ; deliveryAttempt++) {
        try {
          await new Promise<void>((resolve, reject) => {
            const returned = (message: ConsumeMessage) => {
              if (message.properties.messageId === work.attemptId) reject(new Error('Result unroutable'));
            };
            channel.on('return', returned);
            const timer = setTimeout(() => { channel.removeListener('return', returned); reject(new Error('Confirm timeout')); }, 5000);
            channel.sendToQueue('tarot.results.v1', Buffer.from(JSON.stringify(result)),
              { persistent: true, mandatory: true, contentType: 'application/json', messageId: work.attemptId },
              error => { clearTimeout(timer); channel.removeListener('return', returned); error ? reject(error) : resolve(); });
          });
          channel.ack(delivery); break;
        } catch {
          this.deliveryRetries++;
          if (deliveryAttempt >= 5 || controller.signal.aborted) throw new Error('Result delivery unavailable');
          await sleep(Math.min(8000, 500 * 2 ** deliveryAttempt) + Math.random() * 500);
        }
      }
      console.info(JSON.stringify({ event: 'provider-finished', jobId: work.jobId, attemptId: work.attemptId,
        latencyMs: Date.now() - started, outcome: errorCode ?? 'RESULT', totalTokens, reportedCost,
        cancellationLatencyMs: abortedAt === undefined ? undefined : Date.now() - abortedAt }));
    } finally {
      clearTimeout(deadlineTimer); if (leaseTimer) clearInterval(leaseTimer);
      this.active.delete(work.attemptId);
    }
  }
  async onApplicationShutdown() {
    this.stopping = true; this.ready = false;
    for (const controller of this.active.values()) controller.abort();
    // Allow bounded durable result delivery before connection closure/requeue.
    const until = Date.now() + 10000;
    while (this.active.size && Date.now() < until) await sleep(100);
    await this.connection?.close().catch(() => {});
  }
}
