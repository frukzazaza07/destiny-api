import { expect, test, type Page } from "@playwright/test";

type RewardStatus = {
  enabled: boolean; serviceAvailable: boolean; provider: string; adUnitPath: string;
  validAdCompletions: number; requiredAdCompletions: number; deepCreditsPerCompletedBundle: number;
  availableDeepCredits: number; expiresAt: string | null; nextEligibleAt: string | null;
};

function envelope(data: unknown) {
  return JSON.stringify({ success: true, data, error: null, code: "SUCCESS" });
}

async function prepare(page: Page, locale: "en" | "th", provider: "granted" | "closed" | "no-fill" | "error" = "granted") {
  let progress = 0;
  let sessionStarted = false;
  let nonceNumber = 0;
  const status = (): RewardStatus => ({
    enabled: true, serviceAvailable: true, provider: "GOOGLE_AD_MANAGER", adUnitPath: "/123/test",
    validAdCompletions: progress === 3 ? 0 : progress, requiredAdCompletions: 3, deepCreditsPerCompletedBundle: 1,
    availableDeepCredits: progress === 3 ? 1 : 0,
    expiresAt: sessionStarted ? "2026-09-07T12:00:00Z" : null,
    nextEligibleAt: progress === 3 ? "2026-09-07T12:00:00Z" : null,
  });
  await page.setExtraHTTPHeaders({ "CF-IPCountry": "TH" });
  await page.addInitScript((outcome) => {
    window.__tarotRewardedTestProvider = async () => {
      await new Promise((resolve) => setTimeout(resolve, 300));
      return outcome;
    };
  }, provider);
  await page.route("**/api/auth/me", (route) => route.fulfill({ status: 200, contentType: "application/json", body: envelope({ authenticated: false, userId: null, email: null, emailVerified: false, roles: [], premiumDeepActive: false, premiumExpiresAt: null, registrationEnabled: false, serviceAvailable: true }) }));
  await page.route("**/api/auth/csrf", (route) => route.fulfill({ status: 200, contentType: "application/json", body: envelope({ token: "csrf" }) }));
  await page.route("**/api/readings/options", (route) => route.fulfill({ status: 200, contentType: "application/json", body: envelope({ modes: ["STANDARD", "DEEP"], deepReading: { enabled: true, entitled: false, authenticated: false, premiumExpiresAt: null, availableAdEarnedCredits: 0, upgradeUrl: null }, modelTiers: [] }) }));
  await page.route("**/api/rewards/deep/status", (route) => route.fulfill({ status: 200, contentType: "application/json", body: envelope(status()) }));
  await page.route("**/api/rewards/deep/sessions", (route) => { sessionStarted = true; return route.fulfill({ status: 200, contentType: "application/json", body: envelope(status()) }); });
  await page.route("**/api/rewards/deep/attempts", (route) => route.fulfill({ status: 200, contentType: "application/json", body: envelope({ nonce: `signed-attempt-${++nonceNumber}`.padEnd(40, "x"), expiresAt: "2026-09-06T12:10:00Z" }) }));
  await page.route("**/api/rewards/deep/attempts/close", (route) => route.fulfill({ status: 200, contentType: "application/json", body: envelope(status()) }));
  await page.route("**/api/rewards/deep/grants", (route) => { progress++; return route.fulfill({ status: 200, contentType: "application/json", body: envelope(status()) }); });
  await page.goto(`/${locale}`);
  return { progress: () => progress, attempts: () => nonceNumber };
}

test("three explicit completed ads unlock one DEEP reading and only one request stays active", async ({ page }) => {
  const state = await prepare(page, "en");
  await expect(page.getByRole("heading", { name: "Watch 3 optional rewarded ads to unlock one DEEP reading." })).toBeVisible();
  await page.getByRole("button", { name: "Allow available" }).click();
  const watch = page.getByRole("button", { name: "Watch next optional ad" });
  await watch.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByText("1 of 3 rewarded ads completed").first()).toBeVisible();
  await watch.evaluate((element) => {
    (element as HTMLButtonElement).click();
    (element as HTMLButtonElement).click();
  });
  await expect(page.getByText("2 of 3 rewarded ads completed").first()).toBeVisible();
  await watch.click();
  await expect(page.getByText("1 DEEP credit available").first()).toBeVisible();
  expect(state.progress()).toBe(3);
  expect(state.attempts()).toBe(3);
  const deep = page.getByRole("button", { name: /Deep AI/ });
  await deep.click();
  await expect(deep).toHaveAttribute("aria-pressed", "true");
});

test("progress recovers after reload and consent withdrawal prevents another ad request", async ({ page }) => {
  const state = await prepare(page, "en");
  await page.getByRole("button", { name: "Allow available" }).click();
  await page.getByRole("button", { name: "Watch next optional ad" }).click();
  await expect(page.getByText("1 of 3 rewarded ads completed").first()).toBeVisible();
  await page.locator("[data-consent-settings]").click();
  await page.getByLabel("Advertising").uncheck();
  await page.getByRole("button", { name: "Save choices" }).click();
  await page.waitForLoadState("domcontentloaded");
  await expect(page.getByText("1 of 3 rewarded ads completed").first()).toBeVisible();
  await page.getByRole("button", { name: "Watch next optional ad" }).click();
  await expect(page.getByText("Allow advertising in Privacy choices before requesting a rewarded ad.")).toBeVisible();
  expect(state.attempts()).toBe(1);
});

test("Thai copy uses the server threshold and closing an ad never changes progress", async ({ page }) => {
  const state = await prepare(page, "th", "closed");
  await expect(page.getByRole("heading", { name: "ดูโฆษณาแบบให้รางวัล 3 รายการโดยสมัครใจ เพื่อปลดล็อกการอ่าน DEEP 1 ครั้ง" })).toBeVisible();
  await page.getByRole("button", { name: "อนุญาตบริการที่เปิดใช้" }).click();
  await page.getByRole("button", { name: "ดูโฆษณารายการถัดไป" }).click();
  await expect(page.getByText("คุณปิดหรือปฏิเสธโฆษณา ความคืบหน้าไม่เปลี่ยนแปลง")).toBeVisible();
  expect(state.progress()).toBe(0);
  await expect(page.getByText("ดูโฆษณาสำเร็จ 0 จาก 3 รายการ")).toBeVisible();
});

test("declined advertising consent makes no provider or attempt request and STANDARD stays usable", async ({ page }) => {
  await prepare(page, "en");
  await page.getByRole("button", { name: "Decline optional" }).click();
  await page.getByRole("button", { name: "Watch next optional ad" }).click();
  await expect(page.getByText("Allow advertising in Privacy choices before requesting a rewarded ad.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Shuffle Deck" })).toBeEnabled();
});

for (const outcome of ["no-fill", "error"] as const) {
  test(`${outcome} never increments progress`, async ({ page }) => {
    const state = await prepare(page, "en", outcome);
    await page.getByRole("button", { name: "Allow available" }).click();
    await page.getByRole("button", { name: "Watch next optional ad" }).click();
    await expect(page.getByText(outcome === "no-fill" ? "No rewarded ad is available right now. Try again later." : "The rewarded ad could not load. Progress was not changed.")).toBeVisible();
    expect(state.progress()).toBe(0);
  });
}
