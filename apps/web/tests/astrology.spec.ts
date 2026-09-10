import { expect, test } from '@playwright/test';

const envelope = (data: unknown) => ({ success: true, data, error: null, code: 'SUCCESS' });

test('cancel during fingerprinting prevents submission and permits a fresh reading', async ({ page }) => {
  await page.addInitScript(() => {
    const digest = crypto.subtle.digest.bind(crypto.subtle);
    let release: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; });
    Object.assign(window, { releaseReadingDigest: () => release() });
    crypto.subtle.digest = async (...args) => { await gate; return digest(...args); };
  });
  let submissions = 0;
  await page.route('**/api/auth/csrf', route => route.fulfill({ json: envelope({ token: 'test' }) }));
  await page.route('**/api/rewards/deep/status', route => route.fulfill({ json: envelope({ enabled: false }) }));
  await page.route('**/api/readings/options', route => route.fulfill({ json: envelope({ deepReading: { entitled: true }, modelTiers: [] }) }));
  await page.route('**/api/reading-jobs/thai-astrology', route => {
    submissions++;
    return route.fulfill({ status: 202, json: envelope({ jobId: 'canceled-test', state: 'CANCELED', eventId: 1, reading: null }) });
  });
  await page.goto('/en#astrology-consultation');
  const form = page.locator('.astrology-consultation');
  await form.locator('input[type=date]').fill('2000-02-29');
  await form.locator('textarea').fill('Should I change jobs?');
  await form.locator('button[type=submit]').click();
  await form.getByRole('button', { name: 'Cancel astrology reading' }).click();
  await page.evaluate(() => (window as unknown as { releaseReadingDigest: () => void }).releaseReadingDigest());
  // A fresh submission waits behind the old cancellation; only it may reach the API.
  await form.locator('button[type=submit]').click();
  await expect(form.getByRole('alert')).toBeVisible();
  expect(submissions).toBe(1);
});

test('a late heartbeat cannot restore completed status after starting over', async ({ page }) => {
  const job = { jobId: 'heartbeat-test', state: 'COMPLETED', eventId: 3,
    deadline: new Date(Date.now() + 180000).toISOString(), reading: null,
    astrologyReading: { readingType: 'THAI_ASTROLOGY', locale: 'en', sections: {
      overview: 'Reflect on your choices.', analysis: 'No chart is available.', directAnswer: 'Compare your priorities.',
      timing: null, timingExplanation: 'Reliable timing is unavailable.', advice: 'Ask about the role.', dataLimitations: 'No chart was calculated.',
    } } };
  let releaseHeartbeat!: () => void;
  const gate = new Promise<void>(resolve => { releaseHeartbeat = resolve; });
  let heartbeats = 0;
  await page.route('**/api/auth/csrf', route => route.fulfill({ json: envelope({ token: 'test' }) }));
  await page.route('**/api/rewards/deep/status', route => route.fulfill({ json: envelope({ enabled: false }) }));
  await page.route('**/api/readings/options', route => route.fulfill({ json: envelope({ deepReading: { entitled: true }, modelTiers: [] }) }));
  await page.route('**/api/reading-jobs/thai-astrology', route => route.fulfill({ status: 202, json: envelope({ ...job, state: 'QUEUED', eventId: 1, astrologyReading: null }) }));
  await page.route('**/api/reading-jobs/*/heartbeat', async route => {
    heartbeats++;
    await gate;
    await route.fulfill({ json: envelope(job) });
  });
  await page.route('**/api/reading-jobs/*/events', route => route.fulfill({ contentType: 'text/event-stream',
    body: `event: presence\ndata: ${JSON.stringify(envelope({ subscriberId: 'subscriber', heartbeatSeconds: 30 }))}\n\nevent: status\ndata: ${JSON.stringify(envelope(job))}\n\n` }));
  await page.goto('/en#astrology-consultation');
  const form = page.locator('.astrology-consultation');
  await form.locator('input[type=date]').fill('2000-02-29');
  await form.locator('textarea').fill('Should I change jobs?');
  await form.locator('button[type=submit]').click();
  await expect(form.getByText('Compare your priorities.', { exact: true })).toBeVisible();
  await expect.poll(() => heartbeats).toBe(1);
  await form.locator('article button').click();
  const response = page.waitForResponse('**/api/reading-jobs/*/heartbeat');
  releaseHeartbeat();
  await (await response).finished();
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  await expect(form.getByRole('status')).toBeEmpty();
  await expect(form.locator('button[type=submit]')).toBeEnabled();
});

for (const locale of ['en', 'th']) for (const details of [false, true]) {
  test(`astrology ${locale}: ${details ? 'full details' : 'date only'}, recovery and 3D fallback`, async ({ page }) => {
    let calls = 0;
    await page.route('**/api/auth/csrf', route => route.fulfill({ json: envelope({ token: 'test' }) }));
    await page.route('**/api/rewards/deep/status', route => route.fulfill({ json: envelope({ enabled: false }) }));
    await page.route('**/api/readings/options', route => route.fulfill({ json: envelope({ deepReading: { enabled: true, entitled: true, authenticated: true, availableAdEarnedCredits: 0 }, modelTiers: [] }) }));
    const job = { jobId: '550b0a6e-0b8c-41cb-989f-58c78bfd6841', state: 'QUEUED', eventId: 1, deadline: new Date(Date.now() + 180000).toISOString(), reading: null, astrologyReading: null, errorCode: null, readingType: 'THAI_ASTROLOGY' };
    const sections = locale === 'en' ? { overview: 'A reflective consultation', analysis: 'Chart calculations are unavailable.', directAnswer: 'Compare the new role with your priorities.', timing: null, timingExplanation: 'Reliable dates cannot be determined.', advice: 'Ask about responsibilities.', dataLimitations: 'No precise chart has been calculated.' }
      : { overview: 'การปรึกษาเพื่อสะท้อนมุมมอง', analysis: 'ยังไม่มีการคำนวณผังดวง', directAnswer: 'เปรียบเทียบงานใหม่กับสิ่งที่คุณให้ความสำคัญ', timing: null, timingExplanation: 'ไม่สามารถระบุวันที่อย่างน่าเชื่อถือ', advice: 'สอบถามหน้าที่ความรับผิดชอบ', dataLimitations: 'ยังไม่ได้คำนวณผังดวงอย่างละเอียด' };
    await page.route('**/api/reading-jobs/thai-astrology', async route => {
      calls++;
      expect(route.request().postDataJSON().reading).toEqual({ readingType: 'THAI_ASTROLOGY', birthDate: '1995-04-13', birthTime: details ? '00:00' : null, birthPlace: details ? 'Bangkok, Thailand' : null, question: 'Should I change jobs?', locale });
      await route.fulfill({ status: 202, json: envelope(job) });
    });
    await page.route('**/api/reading-jobs/*/events', route => route.fulfill({ contentType: 'text/event-stream', body: `id: 3\nevent: status\ndata: ${JSON.stringify(envelope({ ...job, state: 'COMPLETED', eventId: 3, astrologyReading: { readingType: 'THAI_ASTROLOGY', locale, sections } }))}\n\n` }));
    await page.goto(`/${locale}`);
    await page.getByRole('button', { name: locale === 'en' ? 'Meet the astrology advisor' : 'พบที่ปรึกษาโหราศาสตร์ไทย' }).click();
    const form = page.locator('.astrology-consultation');
    await form.locator('input[type=date]').fill('1995-04-13');
    if (details) { await form.locator('input[type=time]').fill('00:00'); await form.locator('input[maxlength="200"]').fill('  Bangkok, Thailand  '); }
    await form.locator('textarea').fill('Should I change jobs?');
    await form.locator('button[type=submit]').click();
    await expect(form.getByText(sections.directAnswer, { exact: true })).toBeVisible();
    expect(calls).toBe(1);
    await page.getByRole('button', { name: locale === 'en' ? 'Enter 3D shop' : 'เข้าสู่ร้าน 3 มิติ' }).click();
    await expect(page.getByTestId('destiny-shop-canvas').locator('canvas')).toBeVisible();
    await expect(form.getByText(sections.directAnswer, { exact: true })).toBeVisible();
    // A WebGL loss must preserve the completed reading in the accessible view.
    await page.getByTestId('destiny-shop-canvas').locator('canvas').evaluate(canvas => canvas.dispatchEvent(new Event('webglcontextlost', { cancelable: true })));
    await expect(page.getByTestId('destiny-shop-canvas')).toHaveCount(0);
    await expect(form.getByText(sections.directAnswer, { exact: true })).toBeVisible();
    expect(calls).toBe(1);
  });
}

test('astrology keyboard validation, unknown time, failure/retry and explicit cancellation', async ({ page }) => {
  let calls = 0; let cancels = 0;
  const job = { jobId: '550b0a6e-0b8c-41cb-989f-58c78bfd6841', state: 'QUEUED', eventId: 1, deadline: new Date(Date.now() + 180000).toISOString(), reading: null, errorCode: null };
  await page.route('**/api/auth/csrf', route => route.fulfill({ json: envelope({ token: 'test' }) }));
  await page.route('**/api/rewards/deep/status', route => route.fulfill({ json: envelope({ enabled: false }) }));
  await page.route('**/api/readings/options', route => route.fulfill({ json: envelope({ deepReading: { entitled: true }, modelTiers: [] }) }));
  await page.route('**/api/reading-jobs/thai-astrology', route => { calls++; return route.fulfill({ status: calls === 1 ? 503 : 202, json: calls === 1 ? { success: false, error: 'Unavailable' } : envelope(job) }); });
  await page.route('**/api/reading-jobs/*/cancel', route => { cancels++; return route.fulfill({ json: envelope({ ...job, state: 'CANCELED' }) }); });
  await page.route('**/api/reading-jobs/*/events', route => route.fulfill({ contentType: 'text/event-stream', body: `id: 1\nevent: status\ndata: ${JSON.stringify(envelope(job))}\n\n` }));
  await page.goto('/en');
  await page.getByRole('button', { name: 'Meet the astrology advisor' }).focus(); await page.keyboard.press('Enter');
  const form = page.locator('.astrology-consultation');
  await form.locator('textarea').fill('A question');
  await form.locator('button[type=submit]').click(); expect(calls).toBe(0);
  await form.locator('input[type=date]').fill('2000-02-29');
  await form.locator('input[type=time]').fill('08:30'); await form.getByRole('button', { name: 'Birth time unknown' }).click();
  await expect(form.locator('input[type=time]')).toHaveValue('');
  await form.locator('button[type=submit]').click(); await expect(form.getByRole('alert')).toBeVisible();
  await form.getByRole('button', { name: 'Retry astrology reading' }).click();
  await expect(form.getByText('Your consultation is queued…')).toBeVisible();
  await form.getByRole('button', { name: 'Cancel astrology reading' }).click();
  await expect.poll(() => cancels).toBe(1);
  await expect(form.getByText('Consultation canceled.', { exact: true })).toBeVisible();
  expect(calls).toBe(2);
});

test('astrology mobile touch entry keeps the form inside the viewport', async ({ browser, baseURL }) => {
  const context = await browser.newContext({ baseURL, viewport: { width: 390, height: 844 }, hasTouch: true, reducedMotion: 'reduce' });
  const page = await context.newPage();
  try {
    await page.goto('/th');
    await page.getByRole('button', { name: 'เข้าสู่ร้าน 3 มิติ' }).tap();
    await expect(page.getByTestId('destiny-shop-canvas').locator('canvas')).toBeVisible();
    await page.getByRole('button', { name: 'พบที่ปรึกษาโหราศาสตร์ไทย' }).tap();
    await expect(page.locator('.astrology-panel')).toBeVisible();
    await page.locator('.astrology-consultation input[type=date]').fill('1995-04-13');
    await page.locator('.astrology-consultation textarea').fill('ควรเปลี่ยนงานหรือไม่');
    await page.locator('.astrology-consultation button[type=submit]').click({ trial: true });
    const panel = await page.locator('.astrology-panel').boundingBox();
    expect(panel!.x).toBeGreaterThanOrEqual(0); expect(panel!.x + panel!.width).toBeLessThanOrEqual(390);
    await page.screenshot({ path: '.tmp/model-previews/astrology-mobile.png' });
    await page.getByRole('button', { name: 'ออกจากมุมมอง 3 มิติ' }).tap();
    await expect(page.locator('.astrology-consultation input[type=date]')).toHaveValue('1995-04-13');
  } finally { await context.close(); }
});
