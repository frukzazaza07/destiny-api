import test from 'node:test';
import assert from 'node:assert/strict';
import { workSchema, resultSchema, configSchema } from './contracts';
const work = { version: 1, kind: 'REQUEST', jobId: 'a176d43f-7bc8-42dd-845e-fc7e223518a5',
  attemptId: '20b93baf-9105-4032-830d-464541e39119', correlationId: 'a176d43f-7bc8-42dd-845e-fc7e223518a5',
  deadline: '2026-09-08T12:00:00+00:00', providerRef: 'cloud-default' };
test('v1 rejects credentials, unsupported versions and mismatched correlation', () => {
  assert.ok(workSchema.safeParse(work).success);
  for (const invalid of [{ ...work, apiKey: 'secret' }, { ...work, version: 2 }, { ...work, correlationId: work.attemptId }])
    assert.equal(workSchema.safeParse(invalid).success, false);
});
test('result and failure payloads are bounded and disjoint', () => {
  assert.ok(resultSchema.safeParse({ ...work, kind: 'RESULT', body: '{}', errorCode: null }).success);
  assert.ok(resultSchema.safeParse({ ...work, kind: 'FAILURE', body: null, errorCode: 'PROVIDER_FAILED' }).success);
  assert.equal(resultSchema.safeParse({ ...work, kind: 'RESULT', body: 'x'.repeat(262145), errorCode: null }).success, false);
  assert.equal(resultSchema.safeParse({ ...work, kind: 'FAILURE', body: '{}', errorCode: null }).success, false);
});
test('configuration rejects insecure provider transport and unbounded concurrency', () => {
  const config = { BROKER_URI: 'amqp://localhost', API_URL: 'http://localhost:5000', WORKER_KEY: 'x'.repeat(32),
    PROVIDER_URL: 'https://provider.example.test/v1/chat/completions', PROVIDER_KEY: 'test-only', PROVIDER_MODEL: 'test-model' };
  assert.ok(configSchema.safeParse(config).success);
  assert.equal(configSchema.safeParse({ ...config, PROVIDER_URL: 'http://provider.example.test' }).success, false);
  assert.equal(configSchema.safeParse({ ...config, PREFETCH: 1000 }).success, false);
});
