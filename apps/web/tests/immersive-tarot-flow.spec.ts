import { expect, test, type Page } from "@playwright/test";

const envelope = (data: unknown) => ({ success: true, data, error: null, code: "OK" });

async function enterTable(page: Page) {
  await page.getByRole("button", { name: "Enter 3D shop" }).click();
  await expect(page.getByText("Preparing the reception, gallery, and Tarot room…")).toBeHidden({ timeout: 15_000 });
  await page.keyboard.down("w");
  await page.waitForTimeout(3_700);
  await page.keyboard.up("w");
  await page.getByRole("button", { name: "Sit for a Tarot reading" }).click();
  await expect(page.getByRole("region", { name: "Private Tarot consultation", exact: true })).toBeVisible();
  await expect(page.getByRole("dialog", { name: "Private Tarot consultation" })).toHaveCount(0);
}

// These semantic markers follow the meshes, but pointer-events:none means the
// actual click/tap goes through the canvas raycaster, not a DOM action handler.
async function hitMesh(page: Page, name: string, touch = false) {
  const target = page.getByRole("button", { name, exact: true });
  await expect(target).toBeEnabled();
  await expect.poll(async () => (await target.boundingBox())?.x ?? 0).toBeGreaterThan(0);
  const bounds = (await target.boundingBox())!;
  if (touch) await page.touchscreen.tap(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
  else await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
}

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.route("**/api/readings/options", (route) => route.fulfill({
    contentType: "application/json",
    body: JSON.stringify(envelope({
      deepReading: { enabled: true, entitled: false, upgradeUrl: null, authenticated: false, premiumExpiresAt: null, availableAdEarnedCredits: 0 },
      modelTiers: [{ id: "CORE", model: "test-core", available: true }],
    })),
  }));
  await page.route("**/api/rewards/deep/status", (route) => route.fulfill({
    contentType: "application/json",
    body: JSON.stringify(envelope({ enabled: false, serviceAvailable: false, provider: "disabled", adUnitPath: null, validAdCompletions: 0, requiredAdCompletions: 3, deepCreditsPerCompletedBundle: 1, availableDeepCredits: 0, expiresAt: null, nextEligibleAt: null })),
  }));
});

test("2D and in-world views share one server-authoritative STANDARD reading", async ({ page }) => {
  const requests: Array<{ path: string; body: Record<string, unknown> }> = [];
  await page.route("**/api/deck/shuffle", async (route) => {
    requests.push({ path: "shuffle", body: route.request().postDataJSON() });
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(envelope({ sessionId: "session-1", spread: "DAILY_1", cardCount: 6, selectCount: 1 })) });
  });
  await page.route("**/api/deck/session-1/resolve", async (route) => {
    requests.push({ path: "resolve", body: route.request().postDataJSON() });
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(envelope({ cards: [{ position: "TODAY", cardId: "THE_STAR", orientation: "UPRIGHT" }] })) });
  });
  await page.route("**/api/readings/generate", async (route) => {
    requests.push({ path: "generate", body: route.request().postDataJSON() });
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(envelope({
      title: "A steady light", summary: "Clarity grows through a small practical step.", mainTheme: "Renewed direction",
      cards: [{ position: "TODAY", cardId: "THE_STAR", orientation: "UPRIGHT", cardName: "The Star", interpretation: "Make room for a grounded kind of hope." }],
      opportunities: ["Name the next step."], challenges: ["Avoid rushing."], guidance: ["Write down one action."], reflectionQuestion: "What can become simpler today?", closingMessage: "Keep the insight, and choose for yourself.",
      cacheStatus: "MISS", classification: { domain: "GENERAL", intent: "GUIDANCE", confidence: 0.9, personalization: "NONE" }, cacheKey: null,
      readingMode: "STANDARD", generationSource: "RULE_ENGINE", generationModel: null, modelTier: null, inferenceWorker: null, inferenceProvider: null, promptVariant: null, qualityScore: null,
    })) });
  });

  await page.goto("/en");
  await page.getByRole("button", { name: "1 Card Daily" }).click();
  await page.getByRole("button", { name: "Shuffle Deck" }).click();
  await page.getByRole("button", { name: "Card 1" }).click();
  await page.getByRole("button", { name: "Reveal Reading" }).click();
  await expect(page.getByRole("heading", { name: "A steady light" })).toBeVisible();
  expect(requests.map((request) => request.path)).toEqual(["shuffle", "resolve", "generate"]);
  expect(requests[1].body).toEqual({ selectedIndexes: [0] });
  expect(requests[2].body).toMatchObject({ spread: "DAILY_1", locale: "en", readingMode: "STANDARD", modelTier: null, cards: [{ cardId: "THE_STAR" }] });

  await page.getByRole("button", { name: "Enter 3D shop" }).click();
  await expect(page.getByTestId("destiny-shop-canvas")).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText("Preparing the reception, gallery, and Tarot room…")).toBeHidden({ timeout: 15_000 });
  await page.keyboard.down("w");
  await page.waitForTimeout(3_700);
  await page.keyboard.up("w");
  await page.getByRole("button", { name: "Sit for a Tarot reading" }).click();
  await expect(page.getByRole("region", { name: "Private Tarot consultation", exact: true })).toBeVisible();
  await expect(page.getByRole("dialog", { name: "Private Tarot consultation" })).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "A steady light" })).toBeVisible();
  expect(requests).toHaveLength(3);
});

test("WebGL failure keeps the complete direct reading path available", async ({ page }) => {
  await page.addInitScript(() => {
    HTMLCanvasElement.prototype.getContext = () => null;
  });
  await page.goto("/en");
  await expect(page.getByText("This device cannot open the 3D shop")).toBeVisible();
  await expect(page.getByRole("button", { name: "Enter 3D shop" })).toHaveCount(0);
  await page.getByRole("button", { name: "Start Tarot now" }).click();
  await expect(page.locator("#tarot-consultation")).toBeFocused();
  await expect(page.getByRole("button", { name: "Shuffle Deck" })).toBeEnabled();
});

test("shuffle retry resumes at the failed step", async ({ page }) => {
  let attempts = 0;
  await page.route("**/api/deck/shuffle", async (route) => {
    attempts += 1;
    if (attempts === 1) {
      await route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ success: false, data: null, error: "Temporarily unavailable", code: "UNAVAILABLE" }) });
      return;
    }
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(envelope({ sessionId: "retry-session", spread: "DESTINY_3", cardCount: 6, selectCount: 3 })) });
  });
  await page.goto("/en");
  await page.getByRole("button", { name: "Shuffle Deck" }).click();
  await expect(page.locator(".error")).toContainText("Temporarily unavailable");
  await page.getByRole("button", { name: "Retry Shuffle" }).click();
  await expect(page.getByRole("button", { name: "Card 1", exact: true })).toBeEnabled();
  expect(attempts).toBe(2);
});

test("in-world restart cancels a pending shuffle and ignores its stale response", async ({ page }) => {
  await page.route("**/api/deck/shuffle", async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 450));
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(envelope({ sessionId: "stale-session", spread: "DESTINY_3", cardCount: 6, selectCount: 3 })) }).catch(() => undefined);
  });
  await page.goto("/en");
  await page.getByRole("button", { name: "Enter 3D shop" }).click();
  await expect(page.getByText("Preparing the reception, gallery, and Tarot room…")).toBeHidden({ timeout: 15_000 });
  await page.keyboard.down("w");
  await page.waitForTimeout(3_700);
  await page.keyboard.up("w");
  await page.getByRole("button", { name: "Sit for a Tarot reading" }).click();
  await hitMesh(page, "Shuffle Deck");
  await page.getByRole("button", { name: "Start a new reading" }).click();
  await page.waitForTimeout(650);
  await expect(page.locator(".table-hint")).toContainText("Awaiting shuffle");
  await expect(page.getByRole("button", { name: "Card 1", exact: true })).toHaveCount(0);
  await expect(page.locator(".error")).toHaveCount(0);
});

async function mockTableReading(page: Page, selectCount: number, failures: string[] = []) {
  const calls = { shuffle: 0, resolve: 0, generate: 0, selected: [] as number[] };
  const cards = Array.from({ length: selectCount }, (_, index) => ({ position: selectCount === 1 ? "TODAY" : ["PAST", "PRESENT", "FUTURE"][index], cardId: ["THE_STAR", "THE_SUN", "THE_MOON"][index], orientation: index === 1 ? "REVERSED" : "UPRIGHT" }));
  await page.route("**/api/deck/shuffle", async route => {
    calls.shuffle++;
    if (failures.includes("shuffle") && calls.shuffle === 1) return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ success: false, error: "Shuffle unavailable" }) });
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(envelope({ sessionId: "table-session", spread: selectCount === 1 ? "DAILY_1" : "DESTINY_3", cardCount: 78, selectCount })) });
  });
  await page.route("**/api/deck/table-session/resolve", async route => {
    calls.resolve++;
    calls.selected = route.request().postDataJSON().selectedIndexes;
    if (failures.includes("resolve") && calls.resolve === 1) return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ success: false, error: "Resolve unavailable" }) });
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(envelope({ cards })) });
  });
  await page.route("**/api/readings/generate", async route => {
    calls.generate++;
    if (failures.includes("generate") && calls.generate === 1) return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ success: false, error: "Generation unavailable" }) });
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(envelope({ title: "The table reading", summary: "A small step forward.", mainTheme: "Hope", cards: cards.map(card => ({ ...card, cardName: card.cardId.replaceAll("_", " "), interpretation: "Consider your next step." })), opportunities: ["Reflect"], challenges: ["Rushing"], guidance: ["Pause"], reflectionQuestion: "What matters?", closingMessage: "Choose your next step.", readingMode: "STANDARD", classification: {}, cacheStatus: "MISS" })) });
  });
  return calls;
}

for (const mobile of [false, true]) {
  test(`real 3D ${mobile ? "touch three-card" : "mouse daily"} reading, pagination and selection limits`, async ({ browser, baseURL }) => {
    const context = await browser.newContext({ baseURL, viewport: mobile ? { width: 390, height: 844 } : { width: 1280, height: 900 }, hasTouch: mobile, reducedMotion: "reduce" });
    const page = await context.newPage();
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    const count = mobile ? 3 : 1;
    const calls = await mockTableReading(page, count);
    await page.goto("/en");
    await enterTable(page);
    if (!mobile) await page.getByRole("button", { name: "1 Card Daily" }).click();
    await hitMesh(page, "Shuffle Deck", mobile);
    await expect(page.getByRole("button", { name: "Card 1", exact: true })).toBeEnabled();
    await page.screenshot({ path: `.tmp/model-previews/table-${mobile ? "mobile" : "desktop"}-selection.png` });
    await hitMesh(page, "Card 2", mobile);
    await expect(page.getByRole("button", { name: "Card 2, selected 1", exact: true })).toHaveAttribute("aria-pressed", "true");
    await hitMesh(page, "Card 2, selected 1", mobile);
    await expect(page.getByRole("button", { name: "Card 2", exact: true })).toHaveAttribute("aria-pressed", "false");
    await hitMesh(page, "Card 1", mobile);
    if (mobile) {
      await hitMesh(page, "Next cards", true);
      await hitMesh(page, "Card 13", true);
      await hitMesh(page, "Next cards", true);
      await hitMesh(page, "Card 25", true);
      await hitMesh(page, "Card 26", true);
      await expect(page.getByRole("button", { name: "Card 26", exact: true })).toHaveAttribute("aria-pressed", "false");
    } else {
      await hitMesh(page, "Card 2");
      await expect(page.getByRole("button", { name: "Card 2", exact: true })).toHaveAttribute("aria-pressed", "false");
    }
    await hitMesh(page, "Deal & reveal", mobile);
    await expect(page.getByRole("heading", { name: "The table reading" })).toBeVisible();
    expect(calls).toEqual({ shuffle: 1, resolve: 1, generate: 1, selected: mobile ? [0, 12, 24] : [0] });
    await page.screenshot({ path: `.tmp/model-previews/table-${mobile ? "mobile" : "desktop"}-revealed.png` });
    await page.getByRole("button", { name: "Leave consultation" }).click();
    await page.getByRole("button", { name: "Sit for a Tarot reading" }).click();
    await expect(page.getByRole("heading", { name: "The table reading" })).toBeVisible();
    await page.locator("canvas").evaluate(canvas => canvas.dispatchEvent(new Event("webglcontextlost", { cancelable: true })));
    await expect(page.getByTestId("destiny-shop-canvas")).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "The table reading" })).toBeVisible();
    expect(calls.generate).toBe(1);
    expect(errors).toEqual([]);
    await context.close();
  });
}

test("3D keyboard controls and animated retries resume only the failed request", async ({ page }) => {
  test.setTimeout(60_000);
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.emulateMedia({ reducedMotion: "no-preference" });
  const calls = await mockTableReading(page, 3, ["shuffle", "resolve", "generate"]);
  await page.goto("/en");
  await enterTable(page);
  const shuffle = page.getByRole("button", { name: "Shuffle Deck", exact: true });
  await shuffle.focus();
  await page.keyboard.press("Enter");
  await expect(page.locator(".error")).toContainText("Shuffle unavailable");
  await page.getByRole("button", { name: "Retry Shuffle" }).click();
  await expect(page.getByRole("button", { name: "Card 1", exact: true })).toBeEnabled();
  for (const index of [3, 1, 2]) {
    await page.getByRole("button", { name: `Card ${index}`, exact: true }).focus();
    await page.keyboard.press("Enter");
  }
  await page.getByRole("button", { name: "Deal & reveal", exact: true }).focus();
  await page.keyboard.press("Enter");
  await page.keyboard.press("Enter");
  await expect(page.locator(".error")).toContainText("Resolve unavailable");
  await page.getByRole("button", { name: "Retry Reveal" }).click();
  await expect(page.locator(".error")).toContainText("Generation unavailable");
  await page.getByRole("button", { name: "Retry Reading" }).click();
  await expect(page.getByRole("heading", { name: "The table reading" })).toBeVisible();
  expect(calls).toEqual({ shuffle: 2, resolve: 2, generate: 2, selected: [2, 0, 1] });
  expect(errors).toEqual([]);
});

test("Thai table controls work in low quality with reduced motion", async ({ page }) => {
  const calls = await mockTableReading(page, 1);
  await page.goto("/th");
  await page.getByRole("button", { name: "เข้าสู่ร้าน 3 มิติ" }).click();
  await expect(page.getByText("กำลังเตรียมโถงต้อนรับ แกลเลอรี และห้องไพ่ทาโรต์…")).toBeHidden({ timeout: 15_000 });
  await page.getByLabel("คุณภาพภาพ").selectOption("LOW");
  await page.keyboard.down("w");
  await page.waitForTimeout(3_700);
  await page.keyboard.up("w");
  await page.getByRole("button", { name: "นั่งลงเพื่อดูไพ่ทาโรต์" }).click();
  await page.getByRole("button", { name: "สับไพ่", exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("button", { name: "ไพ่ 1", exact: true })).toBeEnabled();
  await hitMesh(page, "ไพ่ 1");
  await hitMesh(page, "แจกและเปิดไพ่");
  await expect(page.getByRole("heading", { name: "The table reading" })).toBeVisible();
  expect(calls).toEqual({ shuffle: 1, resolve: 1, generate: 1, selected: [0] });
  await page.screenshot({ path: ".tmp/model-previews/table-thai-low.png" });
});
