import { apiFetch, apiMutation, apiUrl, readApiData, type ApiEnvelope } from './api-client';

type Job<T> = { jobId: string; state: string; eventId: number; deadline: string; reading: T | null; astrologyReading?: T | null; errorCode: string | null };
const terminal = (state: string) => ['COMPLETED', 'FAILED', 'CANCELED'].includes(state);

/** One client per shared 2D/3D flow. Unmount closes presence; an explicit reset cancels immediately. */
export class ReadingJobClient {
  constructor(private readonly endpoint = '/api/reading-jobs', private readonly resultField: 'reading' | 'astrologyReading' = 'reading', private readonly onState?: (state: string) => void) {}
  private jobId: string | null = null;
  private pendingCreation: Promise<Job<unknown>> | null = null;
  private storageKey: string | null = null;
  private recoverCreation: (() => Promise<Job<unknown>>) | null = null;
  private canceling: Promise<void> = Promise.resolve();
  private cancelOperations: Array<() => Promise<void>> = [];
  cancel() {
    const id = this.jobId;
    const creation = this.pendingCreation;
    const recover = this.recoverCreation;
    if (this.storageKey) sessionStorage.removeItem(this.storageKey);
    this.storageKey = null;
    this.jobId = null;
    this.cancelOperations.push(async () => {
      const target = id ?? (await creation?.catch(error => recover ? recover() : Promise.reject(error)))?.jobId;
      if (target) await readApiData(await apiMutation(`/api/reading-jobs/${target}/cancel`, 'POST'));
    });
    this.canceling = this.canceling.catch(() => {}).then(() => this.drainCancellations());
    // Keep failed operations queued; a subsequent retry must confirm cancellation before submission.
    void this.canceling.catch(() => {});
  }
  private async drainCancellations() {
    while (this.cancelOperations.length) {
      const operation = this.cancelOperations[0];
      await operation();
      if (this.cancelOperations[0] === operation) this.cancelOperations.shift();
    }
  }
  async generate<T>(reading: unknown, signal: AbortSignal, errorMessage: string): Promise<T> {
    await this.canceling.catch(() => this.drainCancellations());
    signal.throwIfAborted();
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(reading)));
    signal.throwIfAborted();
    const fingerprint = Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('');
    const storageKey = `${this.resultField === 'reading' ? 'tarot' : 'astrology'}-job:${fingerprint}`;
    this.storageKey = storageKey;
    // Only an opaque submission key is stored; never questions, card data, or results.
    let key = sessionStorage.getItem(storageKey);
    if (!key) { key = crypto.randomUUID(); sessionStorage.setItem(storageKey, key); }
    const create = async () => readApiData<Job<T>>(await apiMutation(this.endpoint, 'POST', { idempotencyKey: key, reading }));
    this.recoverCreation = create;
    this.pendingCreation = create();
    let job: Job<T>;
    try { job = await this.pendingCreation as Job<T>; }
    catch (error) { this.pendingCreation = null; throw error; }
    this.pendingCreation = null;
    this.jobId = job.jobId;
    if (signal.aborted) this.jobId = null;
    signal.throwIfAborted();
    this.onState?.(job.state);
    if (!terminal(job.state)) job = await this.subscribe<T>(job, signal);
    this.jobId = null;
    const result = job[this.resultField];
    if (job.state === 'COMPLETED' && result) return result;
    sessionStorage.removeItem(storageKey);
    throw new Error(errorMessage);
  }
  private subscribe<T>(initial: Job<T>, signal: AbortSignal): Promise<Job<T>> {
    return new Promise((resolve, reject) => {
      const source = new EventSource(apiUrl(`/api/reading-jobs/${initial.jobId}/events`), { withCredentials: true });
      let last = -1;
      let checking = false;
      let heartbeat: ReturnType<typeof setInterval> | undefined;
      let heartbeating = false;
      let stopped = false;
      const stop = () => { stopped = true; source.close(); clearTimeout(timeout); clearInterval(heartbeat); signal.removeEventListener('abort', abort); };
      const abort = () => { stop(); reject(new DOMException('Aborted', 'AbortError')); };
      const accept = (job: Job<T>) => {
        if (stopped || signal.aborted) return;
        if (job.eventId < last) return;
        last = job.eventId;
        this.onState?.(job.state);
        if (terminal(job.state)) { stop(); resolve(job); }
      };
      const timeout = setTimeout(() => { stop(); reject(new Error('Reading connection expired.')); },
        Math.max(1000, Date.parse(initial.deadline) - Date.now() + 10000));
      signal.addEventListener('abort', abort, { once: true });
      if (signal.aborted) { abort(); return; }
      source.addEventListener('presence', event => {
        if (stopped || signal.aborted) return;
        try {
          const envelope = JSON.parse((event as MessageEvent).data) as ApiEnvelope<{ subscriberId: string; heartbeatSeconds: number }>;
          const presence = envelope.data;
          if (!envelope.success || !presence || typeof presence.subscriberId !== 'string' ||
              !Number.isFinite(presence.heartbeatSeconds) || presence.heartbeatSeconds <= 0) return;
          clearInterval(heartbeat);
          const beat = async () => {
            if (stopped || heartbeating || signal.aborted) return;
            heartbeating = true;
            try {
              accept(await readApiData<Job<T>>(await apiMutation(`/api/reading-jobs/${initial.jobId}/heartbeat`, 'POST', { subscriberId: presence.subscriberId })));
            } catch { /* Failed acknowledgement lets the durable presence lease expire safely. */ }
            finally { heartbeating = false; }
          };
          void beat();
          heartbeat = setInterval(() => { void beat(); }, Math.max(1000, presence.heartbeatSeconds * 1000));
        } catch { /* An invalid presence event cannot extend server-side leases. */ }
      });
      source.addEventListener('status', event => {
        try {
          const envelope = JSON.parse((event as MessageEvent).data) as ApiEnvelope<Job<T>>;
          if (envelope.success && envelope.data) accept(envelope.data);
        } catch { /* Reconnect/status reconciliation recovers malformed or interrupted frames. */ }
      });
      source.onerror = () => {
        clearInterval(heartbeat);
        // EventSource reconnects automatically. A status read also recovers missed terminal notifications.
        if (stopped || checking || signal.aborted) return;
        checking = true;
        void apiFetch(`/api/reading-jobs/${initial.jobId}`, { cache: 'no-store', signal })
          .then(response => readApiData<Job<T>>(response)).then(accept).catch(() => {})
          .finally(() => { checking = false; });
      };
    });
  }
}
