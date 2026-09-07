import { expect, test } from "@playwright/test";

const envelope = (data: unknown) => ({ success: true, data, error: null, code: "OK" });

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
  await expect(page.getByRole("dialog", { name: "Private Tarot consultation" })).toBeVisible();
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
  await page.getByRole("button", { name: "Shuffle Deck" }).click();
  await page.getByRole("button", { name: "Start a new reading" }).click();
  await page.waitForTimeout(650);
  await expect(page.getByText("Awaiting shuffle")).toBeVisible();
  await expect(page.getByRole("button", { name: "Card 1", exact: true })).toBeDisabled();
  await expect(page.locator(".error")).toHaveCount(0);
});
