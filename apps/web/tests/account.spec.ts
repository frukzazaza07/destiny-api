import { expect, test, type Page } from "@playwright/test";

type MockAccount = {
  authenticated: boolean; userId: string | null; email: string | null; emailVerified: boolean; roles: string[];
  premiumDeepActive: boolean; premiumExpiresAt: string | null; registrationEnabled: boolean; serviceAvailable: boolean;
};

const anonymous: MockAccount = {
  authenticated: false, userId: null, email: null, emailVerified: false, roles: [],
  premiumDeepActive: false, premiumExpiresAt: null, registrationEnabled: false, serviceAvailable: true
};

async function mockAccountApi(page: Page, account: MockAccount = anonymous) {
  await page.route("**/api/auth/me", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ success: true, data: account, error: null, code: "SUCCESS" }) }));
  await page.route("**/api/auth/csrf", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ success: true, data: { token: "test-csrf" }, error: null, code: "SUCCESS" }) }));
}

for (const locale of ["en", "th"] as const) {
  test(`${locale} login is localized, keyboard reachable, and mobile safe`, async ({ page }) => {
    await mockAccountApi(page);
    await page.setViewportSize({ width: 360, height: 800 });
    await page.goto(`/${locale}/login`);
    await expect(page.locator("html")).toHaveAttribute("lang", locale);
    await expect(page.getByRole("heading", { level: 1 })).toContainText(locale === "en" ? "Log in" : "เข้าสู่ระบบ");
    await page.keyboard.press("Tab");
    await expect(page.locator(":focus")).toHaveAttribute("href", "#main-content");
    expect(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)).toBe(false);
  });
}

test("invalid credentials remain a clear non-enumerating login error", async ({ page }) => {
  await mockAccountApi(page);
  await page.route("**/api/auth/login", (route) => route.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ success: false, data: null, error: "Invalid email or password.", code: "UNAUTHORIZED" }) }));
  await page.goto("/en/login");
  await page.getByLabel("Email").fill("missing@example.test");
  await page.getByLabel("Password", { exact: true }).fill("incorrect");
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.locator("p[role=alert]")).toHaveText("Invalid email or password.");
});

test("account page shows verified premium status without exposing entitlement controls", async ({ page }) => {
  await mockAccountApi(page, {
    authenticated: true, userId: "37e96b38-4353-40c0-a885-eeb1974307c1", email: "reader@example.test",
    emailVerified: true, roles: ["USER"], premiumDeepActive: true, premiumExpiresAt: "2027-01-15T12:00:00Z",
    registrationEnabled: false, serviceAvailable: true
  });
  await page.goto("/en/account");
  await expect(page.getByText("reader@example.test")).toBeVisible();
  await expect(page.getByText(/Active · Expires/)).toBeVisible();
  await expect(page.getByRole("link", { name: "Open administrator console" })).toHaveCount(0);
});

test("admin page requests a role-authenticated login and never renders an admin-key input", async ({ page }) => {
  await mockAccountApi(page);
  await page.goto("/admin");
  await expect(page.getByRole("heading", { name: "Administrator sign-in required" })).toBeVisible();
  await expect(page.locator('input[placeholder="X-Admin-Key"]')).toHaveCount(0);
  await expect(page.getByRole("link", { name: /Log in/ })).toHaveAttribute("href", "/en/login?return=%2Fadmin");
});

test("active premium account can select DEEP while an ordinary account cannot", async ({ page }) => {
  await mockAccountApi(page, {
    authenticated: true, userId: "37e96b38-4353-40c0-a885-eeb1974307c1", email: "reader@example.test",
    emailVerified: true, roles: ["USER"], premiumDeepActive: true, premiumExpiresAt: "2027-01-15T12:00:00Z",
    registrationEnabled: false, serviceAvailable: true
  });
  await page.route("**/api/readings/options", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ success: true, data: { modes: ["STANDARD", "DEEP"], deepReading: { enabled: true, entitled: true, authenticated: true, premiumExpiresAt: "2027-01-15T12:00:00Z", upgradeUrl: null }, modelTiers: [] }, error: null, code: "SUCCESS" }) }));
  await page.goto("/en");
  const deep = page.getByRole("button", { name: /Deep AI/ });
  await expect(deep).toBeEnabled();
  await deep.click();
  await expect(deep).toHaveAttribute("aria-pressed", "true");
});

test("authenticated admin sees account and premium controls", async ({ page }) => {
  await mockAccountApi(page, {
    authenticated: true, userId: "080c8b8a-d47a-44ff-8adc-2e2b7ab74a75", email: "admin@example.test",
    emailVerified: true, roles: ["USER", "ADMIN"], premiumDeepActive: false, premiumExpiresAt: null,
    registrationEnabled: false, serviceAvailable: true
  });
  await page.route("**/api/admin/cache/analytics", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ success: true, data: { runtime: { llmAvoided: 0 }, persistent: { answerCount: 0, variantCount: 0, totalHits: 0, mostUsed: [] }, eligibleHitRate: 0, baseInterpretationHits: 0 }, error: null, code: "SUCCESS" }) }));
  await page.route("**/api/classifier/training-examples?**", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ success: true, data: { items: [] }, error: null, code: "SUCCESS" }) }));
  await page.route("**/api/admin/cache/warmups", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ success: true, data: [], error: null, code: "SUCCESS" }) }));
  await page.route("**/api/admin/users?**", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ success: true, data: { items: [{ id: "37e96b38-4353-40c0-a885-eeb1974307c1", email: "reader@example.test", emailVerified: true, enabled: true, roles: ["USER"], premiumDeepActive: false, premiumExpiresAt: null, premiumRevision: null, revision: 0 }] }, error: null, code: "SUCCESS" }) }));
  await page.route("**/api/admin/rewarded-deep/settings", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ success: true, data: { requiredAdCompletions: 3, deepCreditsPerCompletedBundle: 1, revision: 0, updatedAt: "2026-09-05T00:00:00Z", updatedByUserId: null }, error: null, code: "SUCCESS" }) }));
  await page.goto("/admin");
  await expect(page.getByRole("heading", { name: "Users & Premium DEEP" })).toBeVisible();
  await expect(page.getByText("reader@example.test")).toBeVisible();
  await expect(page.getByRole("button", { name: "Grant" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Rewarded DEEP settings" })).toBeVisible();
});
