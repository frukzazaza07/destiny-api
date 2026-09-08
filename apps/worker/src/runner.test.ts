import 'reflect-metadata';
import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { Runner } from './runner';

const work = { version: 1, kind: 'REQUEST', jobId: 'a176d43f-7bc8-42dd-845e-fc7e223518a5',
  attemptId: '20b93baf-9105-4032-830d-464541e39119', correlationId: 'a176d43f-7bc8-42dd-845e-fc7e223518a5',
  deadline: new Date(Date.now() + 60000).toISOString(), providerRef: 'cloud-default' };

async function scenario(decision: string, failFirstPublish: boolean, slowProvider = false) {
  let calls = 0; let acknowledged = 0; let publishes = 0; let result: { errorCode: string | null } | undefined;
  const server = createServer((_request, response) => {
    calls++;
    assert.equal(_request.headers['idempotency-key'], work.attemptId);
    if (!slowProvider) { response.writeHead(200); response.end('{}'); }
  });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = (server.address() as { port: number }).port;
  Object.assign(process.env, { BROKER_URI: 'amqp://localhost', API_URL: 'http://localhost:5000',
    WORKER_KEY: 'test-worker-key-example-only-123456', PROVIDER_URL: `http://127.0.0.1:${port}/v1/chat/completions`,
    PROVIDER_KEY: 'example-only', PROVIDER_MODEL: 'test-model', PROVIDER_IDEMPOTENCY_HEADER: 'Idempotency-Key', NODE_ENV: 'test' });
  const runner = new Runner();
  Object.assign(runner, { control: async (_work: unknown, action: string) => ({
    decision: action === 'renew' ? 'SKIP' : decision, providerRef: 'cloud-default', model: 'test-model', request: { messages: [] },
  }) });
  const channel = { on: () => {}, removeListener: () => {}, ack: () => { acknowledged++; }, nack: () => { throw new Error('Unexpected nack'); },
    sendToQueue: (_queue: string, body: Buffer, _options: unknown, callback: (error: Error | null) => void) => {
      publishes++; result = JSON.parse(body.toString()); callback(failFirstPublish && publishes === 1 ? new Error('Broker busy') : null);
    } };
  try {
    await (runner as unknown as { handle(channel: unknown, delivery: unknown): Promise<void> }).handle(channel, { content: Buffer.from(JSON.stringify(work)) });
    return { calls, acknowledged, publishes, result, cancellations: runner.cancellations };
  } finally { server.closeAllConnections(); server.close(); }
}
test('a canceled queued request is acknowledged without a provider call', async () => {
  const outcome = await scenario('SKIP', false);
  assert.equal(outcome.calls, 0); assert.equal(outcome.acknowledged, 1); assert.equal(outcome.publishes, 0);
});
test('result confirmation retries never re-enter provider generation', async () => {
  const outcome = await scenario('EXECUTE', true);
  assert.equal(outcome.calls, 1); assert.equal(outcome.publishes, 2); assert.equal(outcome.acknowledged, 1);
});
test('lease loss aborts an in-flight provider request and publishes only failure', async () => {
  const outcome = await scenario('EXECUTE', false, true);
  assert.equal(outcome.calls, 1); assert.equal(outcome.cancellations, 1); assert.equal(outcome.result?.errorCode, 'ABORTED');
});
