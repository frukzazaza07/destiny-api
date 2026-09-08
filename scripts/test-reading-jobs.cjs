// Isolated integration harness; uses documented test values, never environment files.
const { spawn } = require('node:child_process');
const { createServer } = require('node:http');
const { once } = require('node:events');
const { randomUUID } = require('node:crypto');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '..');
const children = [];
const apiProcesses = [];
const workerProcesses = [];
const workerKey = 'integration-worker-key-example-only-12345';
const apiEnv = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development',
  ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'true',
  ConnectionStrings__Postgres: 'Host=127.0.0.1;Port=55439;Database=tarot_jobs_test;Username=tarot_test;Password=example-only',
  ReadingJobs__Enabled: 'true', ReadingJobs__BrokerUri: 'amqp://tarot_test:example-only@127.0.0.1:56739',
  ReadingJobs__WorkerKey: workerKey, ReadingJobs__Model: 'test-model', ReadingJobs__MaxConcurrency: '1',
  ReadingJobs__RequestsPerMinute: '100', ReadingJobs__TokensPerMinute: '2000000',
  ReadingJobs__ReconnectGraceSeconds: '5', ReadingJobs__HeartbeatSeconds: '1', ReadingJobs__PresenceLeaseSeconds: '2',
  DeepReading__Enabled: 'true', DeepReading__AllowUnentitledInDevelopment: 'true',
  StartupCacheWarmup__Enabled: 'false', Classifier__UseGrpc: 'false',
  TarotCache__ModelVersion: 'test-model',
  LLM__AllowCloudForRequestsWithRawQuestion: 'true',
  Logging__LogLevel__Default: 'Warning' };
function launch(command, args, env, name) {
  const log = fs.openSync(path.join(root, `${name}.log`), 'w');
  const child = spawn(command, args, { cwd: root, env, windowsHide: true, stdio: ['ignore', log, log] });
  fs.closeSync(log); children.push(child); return child;
}
const pause = ms => new Promise(r => setTimeout(r, ms));
async function until(check, timeout = 20000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) { if (await check()) return; await pause(200); }
  throw new Error('Integration condition timed out');
}
async function healthy(port) { try { return (await fetch(`http://127.0.0.1:${port}/health`)).ok; } catch { return false; } }
async function client(port = 3511) {
  const base = `http://127.0.0.1:${port}`;
  const response = await fetch(`${base}/api/auth/csrf`, { headers: { 'X-Forwarded-Proto': 'https' } });
  const token = (await response.json()).data.token;
  const cookie = response.headers.getSetCookie().map(s => s.split(';')[0]).join('; ') + `; __Host-Tarot.Reward=${randomUUID()}`;
  return { base, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token, Cookie: cookie, 'X-Forwarded-Proto': 'https' } };
}
const reading = { question: 'What should I reflect on today?', spread: 'DAILY_1', locale: 'en', readingMode: 'DEEP', modelTier: 'CLOUD',
  cards: [{ position: 'GUIDANCE', cardId: 'THE_STAR', orientation: 'UPRIGHT' }] };
async function create(c) {
  const response = await fetch(`${c.base}/api/reading-jobs`, { method: 'POST', headers: c.headers,
    body: JSON.stringify({ idempotencyKey: randomUUID(), reading }) });
  const body = await response.json();
  assert.equal(response.status, 202, JSON.stringify(body)); return body.data;
}
async function get(c, id) { const r = await fetch(`${c.base}/api/reading-jobs/${id}`, { headers: c.headers }); return (await r.json()).data; }
async function cancel(c, id) { return fetch(`${c.base}/api/reading-jobs/${id}/cancel`, { method: 'POST', headers: c.headers }); }
function subscribe(c, id, port = 3512) {
  const abort = new AbortController(); const events = [];
  let heartbeat;
  const done = (async () => {
    const r = await fetch(`http://127.0.0.1:${port}/api/reading-jobs/${id}/events`, { headers: c.headers, signal: abort.signal });
    assert.equal(r.status, 200);
    let buffer = '';
    for await (const bytes of r.body) {
      buffer += Buffer.from(bytes).toString();
      let split;
      while ((split = buffer.indexOf('\n\n')) >= 0) {
        const frame = buffer.slice(0, split); buffer = buffer.slice(split + 2);
        const data = frame.split('\n').find(x => x.startsWith('data: '));
        if (data && frame.includes('event: presence')) {
          const presence = JSON.parse(data.slice(6)).data;
          clearInterval(heartbeat);
          const beat = () => fetch(`${c.base}/api/reading-jobs/${id}/heartbeat`, { method: 'POST', headers: c.headers,
            body: JSON.stringify({ subscriberId: presence.subscriberId }), signal: abort.signal }).catch(() => {});
          void beat(); heartbeat = setInterval(beat, presence.heartbeatSeconds * 1000);
        } else if (data) events.push(JSON.parse(data.slice(6)).data);
      }
    }
  })().catch(e => { if (!abort.signal.aborted) throw e; }).finally(() => clearInterval(heartbeat));
  return { abort, events, done };
}
let calls = 0; let aborted = 0; let providerMode = 'success';
const server = createServer(async (req, res) => {
  let raw = ''; for await (const part of req) raw += part;
  const input = JSON.parse(raw); calls++;
  if (providerMode === 'wait') { res.on('close', () => { aborted++; }); return; }
  const cards = JSON.parse(input.messages[1].content).cards;
  const content = { title: 'A hopeful direction', summary: 'Consider your choices thoughtfully.', mainTheme: 'Reflection and hope',
    cards: cards.map(x => ({ position: x.position, cardId: x.cardId, interpretation: 'Consider what renewed hope means for your choices.' })),
    opportunities: ['Make space for reflection'], challenges: ['Avoid assuming certainty'], guidance: ['Take one practical step'],
    reflectionQuestion: 'What choice feels aligned with your values?', closingMessage: 'Use this reading as reflection.' };
  res.writeHead(200, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify({ choices: [{ message: { content: JSON.stringify(content) } }] }));
});
async function main() {
  server.listen(3513, '127.0.0.1'); await once(server, 'listening');
  const migrate = launch('dotnet', ['apps/api/bin/Debug/net9.0/TarotDestiny.Api.dll', '--migrate'], apiEnv, '.jobs-migrate');
  assert.equal((await once(migrate, 'exit'))[0], 0);
  for (const port of [3511, 3512]) apiProcesses.push(launch('dotnet', ['apps/api/bin/Debug/net9.0/TarotDestiny.Api.dll'],
    { ...apiEnv, ASPNETCORE_URLS: `http://127.0.0.1:${port}` }, `.jobs-api-${port}`));
  await until(async () => await healthy(3511) && await healthy(3512));
  const doc = await (await fetch('http://127.0.0.1:3511/swagger/v1/swagger.json')).json();
  assert.ok(doc.paths['/api/reading-jobs/{id}/events'].get.responses['200'].content['text/event-stream']);
  assert.ok((await (await fetch('http://127.0.0.1:3511/swagger/index.html')).text()).includes('swagger-ui'));
  console.log('PASS Development API Swagger JSON and UI');
  const queuedClient = await client(); const queued = await create(queuedClient);
  await until(async () => (await get(queuedClient, queued.jobId)).state === 'CANCELED', 10000);
  const workerEnv = { ...process.env, NODE_ENV: 'development', BROKER_URI: apiEnv.ReadingJobs__BrokerUri,
    API_URL: 'http://127.0.0.1:3511', WORKER_KEY: workerKey, PROVIDER_URL: 'http://127.0.0.1:3513/v1/chat/completions',
    PROVIDER_KEY: 'example-only', PROVIDER_MODEL: 'test-model', PREFETCH: '2' };
  for (const port of [3514, 3515]) workerProcesses.push(launch(process.execPath, ['apps/worker/dist/main.js'], { ...workerEnv, PORT: String(port) }, `.jobs-worker-${port}`));
  await until(async () => { try { return (await fetch('http://127.0.0.1:3514/ready')).ok; } catch { return false; } });
  await pause(1000); assert.equal(calls, 0); console.log('PASS queued disconnect makes zero provider calls');
  const amqp = require('../apps/worker/node_modules/amqplib');
  const broker = await amqp.connect(apiEnv.ReadingJobs__BrokerUri);
  try {
    const channel = await broker.createConfirmChannel();
    const before = (await channel.checkQueue('tarot.dead.v1')).messageCount;
    for (const queue of ['tarot.requests.v1', 'tarot.results.v1'])
      await new Promise((resolve, reject) => channel.sendToQueue(queue, Buffer.from('{"version":999}'), { persistent: true }, error => error ? reject(error) : resolve()));
    await until(async () => (await channel.checkQueue('tarot.dead.v1')).messageCount >= before + 2);
    await channel.close(); console.log('PASS invalid request/result messages dead-letter without provider calls');
  } finally { await broker.close(); }
  assert.ok((await (await fetch('http://127.0.0.1:3514/swagger/v1/swagger.json')).json()).paths['/ready']);
  assert.ok((await (await fetch('http://127.0.0.1:3514/swagger')).text()).includes('swagger-ui'));
  console.log('PASS Development worker Swagger JSON and UI');
  const c = await client(); const job = await create(c); const stream = subscribe(c, job.jobId);
  await until(async () => (await get(c, job.jobId)).state === 'COMPLETED'); await stream.done;
  assert.equal(stream.events.at(-1).state, 'COMPLETED'); assert.equal(calls, 1);
  assert.ok(stream.events.every((x, i, a) => i === 0 || x.eventId > a[i - 1].eventId));
  const stranger = await client();
  assert.equal((await fetch(`${stranger.base}/api/reading-jobs/${job.jobId}`, { headers: stranger.headers })).status, 404);
  await cancel(c, job.jobId); assert.equal((await get(c, job.jobId)).state, 'COMPLETED');
  console.log('PASS C# -> RabbitMQ -> TS -> provider -> RabbitMQ -> C# -> SSE on second API replica');
  providerMode = 'wait';
  const a = await client(); const ja = await create(a); const sa = subscribe(a, ja.jobId);
  await until(async () => calls === 2);
  const b = await client(); const jb = await create(b); const sb = subscribe(b, jb.jobId);
  await pause(2500); assert.equal(calls, 2); // second worker cannot multiply shared concurrency
  await cancel(a, ja.jobId); await until(async () => aborted >= 1); await sa.done;
  await until(async () => calls === 3);
  sb.abort.abort(); await sb.done;
  await until(async () => (await get(b, jb.jobId)).state === 'CANCELED', 12000);
  await until(async () => aborted >= 2);
  console.log('PASS shared provider concurrency, explicit cancel, last disconnect and transport abort');
  // A broker restart must recover accepted outbox work without requiring another submission.
  const stopBroker = launch('docker', ['stop', 'destiny-jobs-test-broker'], process.env, '.jobs-broker-stop');
  assert.equal((await once(stopBroker, 'exit'))[0], 0);
  const recoveryClient = await client(); const recoveryJob = await create(recoveryClient);
  const recoveryStream = subscribe(recoveryClient, recoveryJob.jobId);
  providerMode = 'success';
  const startBroker = launch('docker', ['start', 'destiny-jobs-test-broker'], process.env, '.jobs-broker-start');
  assert.equal((await once(startBroker, 'exit'))[0], 0);
  await until(async () => (await get(recoveryClient, recoveryJob.jobId)).state === 'COMPLETED', 30000);
  await recoveryStream.done;
  assert.equal(calls, 4);
  console.log('PASS broker restart, publisher reconnect and transactional outbox recovery');
  providerMode = 'wait';
  const silentClient = await client(); const silentJob = await create(silentClient);
  const silentStream = await fetch(`${silentClient.base}/api/reading-jobs/${silentJob.jobId}/events`, { headers: silentClient.headers });
  // Leave the TCP/SSE stream open but never acknowledge browser heartbeats.
  await until(async () => (await get(silentClient, silentJob.jobId)).state === 'CANCELED', 12000);
  await silentStream.body.cancel();
  console.log('PASS missing browser heartbeat cancels even with an open server stream');
  const { chromium } = require('../apps/web/node_modules/@playwright/test');
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    providerMode = 'wait';
    const browserClient = await client(); const browserJob = await create(browserClient);
    const context = await browser.newContext({ extraHTTPHeaders: browserClient.headers });
    const page = await context.newPage();
    await page.goto(`${browserClient.base}/health`);
    await page.evaluate(id => { window.jobEvents = []; const source = new EventSource(`/api/reading-jobs/${id}/events`);
      source.addEventListener('status', e => window.jobEvents.push(JSON.parse(e.data).data.state));
      source.addEventListener('presence', e => { const presence = JSON.parse(e.data).data;
        const beat = () => fetch(`/api/reading-jobs/${id}/heartbeat`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ subscriberId: presence.subscriberId }) });
        void beat(); setInterval(beat, presence.heartbeatSeconds * 1000);
      }); }, browserJob.jobId);
    await until(async () => (await get(browserClient, browserJob.jobId)).state === 'RUNNING');
    await context.close();
    await until(async () => (await get(browserClient, browserJob.jobId)).state === 'CANCELED', 12000);
    console.log('PASS real Chrome page-close cancellation via expiring SSE presence');
  } finally { await browser.close(); }
  // Crash with an unknown provider outcome: lease loss fails closed and never generates a second answer.
  const crashClient = await client(); const crashJob = await create(crashClient); const crashStream = subscribe(crashClient, crashJob.jobId);
  await until(async () => (await get(crashClient, crashJob.jobId)).state === 'RUNNING');
  const beforeCrash = calls;
  for (const process of workerProcesses) process.kill('SIGKILL');
  await until(async () => (await get(crashClient, crashJob.jobId)).state === 'FAILED', 22000); await crashStream.done;
  launch(process.execPath, ['apps/worker/dist/main.js'], { ...workerEnv, PORT: '3514' }, '.jobs-worker-restarted');
  await pause(2000); assert.equal(calls, beforeCrash);
  console.log('PASS worker crash and stale redelivery without duplicate generation');
  apiProcesses[0].kill('SIGKILL');
  await once(apiProcesses[0], 'exit');
  launch('dotnet', ['apps/api/bin/Debug/net9.0/TarotDestiny.Api.dll'], { ...apiEnv, ASPNETCORE_URLS: 'http://127.0.0.1:3511' }, '.jobs-api-restarted');
  await until(() => healthy(3511));
  assert.equal((await get(c, job.jobId)).state, 'COMPLETED');
  console.log('PASS API restart recovers an authorized completed reading');
  const production = launch('dotnet', ['apps/api/bin/Debug/net9.0/TarotDestiny.Api.dll'],
    { ...apiEnv, ASPNETCORE_ENVIRONMENT: 'Production', ASPNETCORE_URLS: 'http://127.0.0.1:3516' }, '.jobs-api-production');
  await until(() => healthy(3516));
  assert.equal((await fetch('http://127.0.0.1:3516/swagger/v1/swagger.json')).status, 404);
  assert.equal((await fetch('http://127.0.0.1:3516/swagger/index.html')).status, 404);
  launch(process.execPath, ['apps/worker/dist/main.js'], { ...workerEnv, NODE_ENV: 'production', PORT: '3517' }, '.jobs-worker-production');
  await until(() => healthy(3517));
  assert.equal((await fetch('http://127.0.0.1:3517/swagger/v1/swagger.json')).status, 404);
  assert.equal((await fetch('http://127.0.0.1:3517/swagger')).status, 404);
  production.kill(); console.log('PASS Production API and worker documentation disabled');
}
main().catch(e => { console.error(e.message); process.exitCode = 1; }).finally(async () => {
  for (const child of children) if (child.exitCode === null) child.kill();
  server.closeAllConnections(); server.close();
});
