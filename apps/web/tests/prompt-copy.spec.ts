import { test, expect, type Page } from "@playwright/test";

const envelope = (data: unknown) => ({ success: true, data, error: null, code: "SUCCESS" });
const readingId = "8e0a5aa2-8f15-4320-bdd0-c59487a56436";
const prompt = "=== SYSTEM ===\nYou are a reflective Tarot adviser. Answer in plain text.\n\n=== USER ===\nA complete reading context.";

async function prepare(page: Page, locale: "en" | "th", loggedIn = true, outcome = "completed") {
  let completed = 0;
  let authenticated = loggedIn;
  let copies = 0;
  let attempts = 0;
  let closes = 0;
  const status = () => ({ readingId, completedAds: completed, requiredAds: 2, unlocked: completed >= 2, available: true });
  const reading = { promptReadingId: readingId, title: "A saved reading", summary: "Reflect on your choices.", mainTheme: "Hope",
    cards: [{ position: "GUIDANCE", cardId: "THE_STAR", orientation: "REVERSED", cardName: "The Star", interpretation: "Consider a next step." }],
    opportunities: ["Reflect"], challenges: ["Avoid certainty"], guidance: ["Take a small step"], reflectionQuestion: "What matters?", closingMessage: "Choose for yourself.",
    cacheStatus: "HIT", classification: { domain: "GENERAL", intent: "UNCLASSIFIED" }, readingMode: "STANDARD", generationSource: "RULE_ENGINE" };
  await page.setExtraHTTPHeaders({ "CF-IPCountry": "TH" });
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.addInitScript(({ locale, reading, outcome }) => {
    if (!sessionStorage.getItem(`tarot-reading:${locale}`)) sessionStorage.setItem(`tarot-reading:${locale}`,
      JSON.stringify({ reading, spread: "DAILY_1", question: "My question", questionMode: "QUESTION", topic: "GENERAL" }));
    window.tarotPromptRewardedProvider = async () => {
      if (outcome === "completed") await fetch("/test-provider-confirm", { method: "POST" });
      return outcome as "completed" | "closed" | "no-fill" | "error";
    };
    Object.defineProperty(navigator, "clipboard", { value: { writeText: async () => { throw new Error("Denied"); } }, configurable: true });
  }, { locale, reading, outcome });
  await page.route("**/test-provider-confirm", async route => { completed++; await route.fulfill({ json: {} }); });
  await page.route("**/api/auth/me", route => route.fulfill({ json: envelope({ authenticated, roles: [], registrationEnabled: false, serviceAvailable: true }) }));
  await page.route("**/api/auth/csrf", route => route.fulfill({ json: envelope({ token: "test-csrf" }) }));
  await page.route("**/api/auth/login", route => { authenticated = true; return route.fulfill({ json: envelope({ authenticated: true }) }); });
  await page.route("**/api/readings/options", route => route.fulfill({ json: envelope({ deepReading: { enabled: false, entitled: false, authenticated, availableAdEarnedCredits: 0, premiumExpiresAt: null }, modelTiers: [] }) }));
  await page.route("**/api/rewards/deep/status", route => route.fulfill({ json: envelope({ enabled: false }) }));
  await page.route("**/api/readings/*/prompt/status", route => route.fulfill({ status: authenticated ? 200 : 401, json: envelope(status()) }));
  await page.route("**/api/readings/*/prompt/sessions", route => route.fulfill({ json: envelope(status()) }));
  await page.route("**/api/readings/*/prompt/attempts", route => {
    attempts++;
    return route.fulfill({ json: envelope({ attemptId: `attempt-${attempts}`, expiresAt: "2099-01-01T00:00:00Z" }) });
  });
  await page.route("**/api/readings/*/prompt/attempts/*/close", route => { closes++; return route.fulfill({ json: envelope(status()) }); });
  await page.route("**/api/readings/*/prompt/copy", route => {
    copies++;
    expect(completed).toBe(2);
    expect(route.request().headers()["x-csrf-token"]).toBe("test-csrf");
    return route.fulfill({ json: envelope({ prompt }) });
  });
  await page.goto(`/${locale}`);
  return { attempts: () => attempts, copies: () => copies, completed: () => completed, closes: () => closes };
}

for (const locale of ["en", "th"] as const) {
  const button = locale === "en" ? "Copy prompt for another AI fortune teller" : "คัดลอก prompt ถึงหมอดู AI อื่น";
  const watch = locale === "en" ? "Watch next optional ad" : "ดูโฆษณารายการถัดไป";
  test(`${locale}: two ads, refresh recovery, clipboard fallback, and repeated copy`, async ({ page }) => {
    const state = await prepare(page, locale);
    await page.getByRole("button", { name: locale === "en" ? "Allow available" : "อนุญาตบริการที่เปิดใช้" }).click();
    await page.getByRole("button", { name: button }).click();
    await page.getByRole("button", { name: watch }).click();
    await expect(page.getByText(locale === "en" ? "1 of 2 rewarded ads completed" : "ดูโฆษณาสำเร็จ 1 จาก 2 รายการ").first()).toBeVisible();
    expect(state.copies()).toBe(0);
    await page.reload();
    await page.getByRole("button", { name: button }).click();
    await expect(page.getByText(locale === "en" ? "1 of 2 rewarded ads completed" : "ดูโฆษณาสำเร็จ 1 จาก 2 รายการ").first()).toBeVisible();
    await page.getByRole("button", { name: watch }).click();
    await expect.poll(state.completed).toBe(2);
    await expect(page.getByRole("button", { name: button })).toBeEnabled();
    await page.getByRole("button", { name: button }).click();
    await expect(page.getByRole("textbox", { name: button })).toHaveValue(prompt);
    await page.getByRole("button", { name: button }).click();
    await expect.poll(state.copies).toBe(2);
    expect(state.attempts()).toBe(2);
    {
      await page.getByRole("button", { name: locale === "en" ? "Enter 3D shop" : "เข้าสู่ร้าน 3 มิติ" }).click();
      await expect(page.locator(".scene-loading")).toBeHidden({ timeout: 15000 });
      await page.keyboard.down("w"); await page.waitForTimeout(3700); await page.keyboard.up("w");
      await page.getByRole("button", { name: locale === "en" ? "Sit for a Tarot reading" : "นั่งลงเพื่อดูไพ่ทาโรต์" }).click();
      await expect(page.getByRole("button", { name: button })).toBeVisible();
      await page.evaluate(() => Object.defineProperty(navigator, "clipboard", { value: { writeText: async (text: string) => {
        document.body.dataset.copiedPrompt = text;
      } }, configurable: true }));
      await page.getByRole("button", { name: button }).click();
      await expect(page.getByText(locale === "en" ? "Prompt copied. Paste it into your chosen AI." : "คัดลอก prompt แล้ว นำไปวางใน AI ที่คุณเลือกได้เลย")).toBeVisible();
      await expect(page.locator("body")).toHaveAttribute("data-copied-prompt", prompt);
    }
  });
}

for (const outcome of ["closed", "no-fill", "error"]) {
  test(`${outcome} leaves prompt locked`, async ({ page }) => {
    const state = await prepare(page, "en", true, outcome);
    await page.getByRole("button", { name: "Allow available" }).click();
    await page.getByRole("button", { name: "Copy prompt for another AI fortune teller" }).click();
    await page.getByRole("button", { name: "Watch next optional ad" }).click();
    await expect.poll(state.closes).toBe(1);
    expect(state.completed()).toBe(0); expect(state.copies()).toBe(0);
  });
}

test("login preserves reading and consent is required before any ad", async ({ page }) => {
  const state = await prepare(page, "en", false);
  await page.getByRole("button", { name: "Decline optional" }).click();
  await page.getByRole("button", { name: "Copy prompt for another AI fortune teller" }).click();
  await page.getByRole("link", { name: "Log in to unlock this reading’s prompt" }).click();
  await page.getByLabel("Email", { exact: true }).fill("copy@example.test");
  await page.getByLabel("Password", { exact: true }).fill("Strong-copy-123!");
  await page.getByRole("button", { name: "Continue", exact: true }).click();
  await expect(page.getByRole("heading", { name: "A saved reading" })).toBeVisible();
  await page.getByRole("button", { name: "Copy prompt for another AI fortune teller" }).click();
  await page.getByRole("button", { name: "Watch next optional ad" }).click();
  await expect(page.getByText("Allow advertising in Privacy choices before requesting a rewarded ad.")).toBeVisible();
  expect(state.attempts()).toBe(0);
});
