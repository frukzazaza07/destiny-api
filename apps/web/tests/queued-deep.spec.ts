import { test, expect } from '@playwright/test';

const envelope = (data: unknown) => ({ success: true, data, error: null, code: 'SUCCESS' });
for (const outcome of ['complete', 'cancel'] as const) {
for (const locale of ['en', 'th'] as const) {
  test(`${locale} queued DEEP ${outcome} over SSE`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    const cards = [{ position: 'GUIDANCE', cardId: 'THE_STAR', orientation: 'UPRIGHT' }];
    let submissions = 0;
    let cancellations = 0;
    let heartbeats = 0;
    await page.route('**/api/readings/options', route => route.fulfill({ json: envelope({
      deepReading: { enabled: true, entitled: true, authenticated: true, premiumExpiresAt: '2099-01-01T00:00:00Z', availableAdEarnedCredits: 0 },
      modelTiers: [{ id: 'CLOUD', model: 'test-cloud', available: true }],
    }) }));
    await page.route('**/api/rewards/deep/status', route => route.fulfill({ json: envelope({ enabled: false }) }));
    await page.route('**/api/auth/csrf', route => route.fulfill({ json: envelope({ token: 'test-csrf' }) }));
    await page.route('**/api/deck/shuffle', route => route.fulfill({ json: envelope({ sessionId: 'queued-session', spread: 'DAILY_1', deckSize: 2, selectCount: 1, expiresAt: '2099-01-01T00:00:00Z' }) }));
    await page.route('**/api/deck/*/resolve', route => route.fulfill({ json: envelope({ cards }) }));
    const job = { jobId: 'e6d87f15-fce7-450d-9fe2-d9ce12fa5081', state: 'QUEUED', eventId: 1, deadline: new Date(Date.now() + 180000).toISOString(), reading: null, errorCode: null };
    const presence = { subscriberId: '550b0a6e-0b8c-41cb-989f-58c78bfd6841', heartbeatSeconds: 1 };
    await page.route('**/api/reading-jobs/*/heartbeat', async route => {
      heartbeats++;
      expect(route.request().postDataJSON()).toEqual({ subscriberId: presence.subscriberId });
      expect(route.request().headers()['x-csrf-token']).toBe('test-csrf');
      await route.fulfill({ json: envelope(job) });
    });
    await page.route('**/api/reading-jobs/*/cancel', async route => {
      cancellations++;
      await route.fulfill({ json: envelope({ ...job, state: 'CANCELED', eventId: 3 }) });
    });
    await page.route('**/api/reading-jobs/*', route => route.fulfill({ json: envelope(job) }));
    await page.route('**/api/reading-jobs', async route => {
      submissions++;
      expect(route.request().postDataJSON().reading).toMatchObject({ readingMode: 'DEEP', modelTier: 'CLOUD', locale });
      expect(route.request().headers()['x-csrf-token']).toBe('test-csrf');
      await route.fulfill({ status: 202, json: envelope(job) });
    });
    const result = {
      title: locale === 'en' ? 'A saved cloud reading' : 'คำอ่านไพ่ที่บันทึกแล้ว', summary: 'Reflect on your choices.', mainTheme: 'Hope',
      cards: [{ ...cards[0], cardName: 'The Star', interpretation: 'Consider a hopeful next step.' }],
      opportunities: ['Reflect'], challenges: ['Avoid certainty'], guidance: ['Take a small step'], reflectionQuestion: 'What matters?', closingMessage: 'Choose for yourself.',
      cacheStatus: 'SKIPPED', classification: { domain: 'GENERAL', intent: 'UNCLASSIFIED' }, readingMode: 'DEEP', generationSource: 'LLM', generationModel: 'test-cloud',
    };
    await page.route('**/api/reading-jobs/*/events', route => route.fulfill({ contentType: 'text/event-stream',
      body: `event: presence\ndata: ${JSON.stringify(envelope(presence))}\n\nid: 3\nevent: status\ndata: ${JSON.stringify(envelope(outcome === 'cancel' ? job : { ...job, state: 'COMPLETED', eventId: 3, reading: result }))}\n\n` }));
    await page.goto(`/${locale}`);
    await page.getByRole('button', { name: locale === 'en' ? /Deep AI/ : /วิเคราะห์เชิงลึก/ }).click();
    await page.getByRole('button', { name: locale === 'en' ? '1 Card Daily' : 'ไพ่ประจำวัน 1 ใบ' }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Shuffle Deck' : 'สับไพ่' }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Card 1' : 'ไพ่ใบที่ 1', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Reveal Reading' : 'เปิดคำทำนาย' }).click();
    if (outcome === 'cancel') {
      await expect.poll(() => submissions).toBe(1);
      await expect.poll(() => heartbeats).toBeGreaterThan(0);
      await page.getByRole('button', { name: locale === 'en' ? 'Cancel reading' : 'ยกเลิกการอ่านไพ่', exact: true }).click();
      await expect.poll(() => cancellations).toBe(1);
      await expect(page.getByRole('button', { name: locale === 'en' ? 'Shuffle Deck' : 'สับไพ่' })).toBeEnabled();
      return;
    }
    await expect(page.getByRole('heading', { name: result.title })).toBeVisible();
    expect(submissions).toBe(1);
    if (locale === 'en') {
      await page.getByRole('button', { name: 'Enter 3D shop' }).click();
      await expect(page.getByText('Preparing the reception, gallery, and Tarot room…')).toBeHidden({ timeout: 15000 });
      await page.keyboard.down('w'); await page.waitForTimeout(3700); await page.keyboard.up('w');
      await page.getByRole('button', { name: 'Sit for a Tarot reading' }).click();
      await expect(page.getByRole('heading', { name: result.title })).toBeVisible();
      expect(submissions).toBe(1);
    }
  });
}
}
