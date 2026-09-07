import { expect, test } from "@playwright/test";

const GOOGLE_HOST = /(google(?:tagmanager|syndication|adservices)|doubleclick|fundingchoicesmessages)\.com$/i;
const SITEMAP_ORIGIN = "https://tarot.example.test";
const SITEMAP_PATHS = [
  "",
  "/guides",
  "/about",
  "/contact",
  "/privacy",
  "/terms",
  "/cookie-policy",
  "/disclaimer"
] as const;

function readXmlAttribute(attributes: string, name: string): string {
  const match = attributes.match(new RegExp(`\\b${name}="([^"]+)"`));
  if (!match) throw new Error(`Missing ${name} in sitemap alternate link.`);
  return match[1];
}

function sitemapUrl(locale: "th" | "en", path: string): string {
  return `${SITEMAP_ORIGIN}/${locale}${path || "/"}`;
}

test("root permanently redirects to Thai", async ({ request }) => {
  const response = await request.get("/", { maxRedirects: 0 });
  expect(response.status()).toBe(308);
  expect(response.headers().location).toBe("/th/");
});

for (const locale of ["th", "en"] as const) {
  test(`${locale} reading route has localized metadata and no Google requests`, async ({
    browser
  }) => {
    const context = await browser.newContext({
      extraHTTPHeaders: { "CF-IPCountry": locale === "th" ? "TH" : "US" }
    });
    const page = await context.newPage();
    const googleRequests: string[] = [];
    page.on("request", (request) => {
      const host = new URL(request.url()).hostname;
      if (GOOGLE_HOST.test(host)) googleRequests.push(request.url());
    });

    try {
      const navigation = await page.goto(`/${locale}`);
      expect(navigation?.headers()["cache-control"]).toMatch(/private|no-store/i);
      await expect(page.locator("html")).toHaveAttribute("lang", locale);
      await expect(page.locator('link[rel="canonical"]')).toHaveAttribute(
        "href",
        `https://tarot.example.test/${locale}/`
      );
      await expect(page.locator('meta[property="og:url"]')).toHaveAttribute(
        "content",
        `https://tarot.example.test/${locale}/`
      );
      await expect(
        page.locator('meta[name="google-adsense-account"]')
      ).toHaveAttribute("content", "ca-pub-0000000000000001");
      await expect(page.locator('a[href="/admin"]')).toHaveCount(0);
      expect(googleRequests).toEqual([]);
    } finally {
      await context.close();
    }
  });
}

test("public navigation and every localized trust route are reachable", async ({ page }) => {
  await page.goto("/en");
  for (const route of [
    "about",
    "contact",
    "privacy",
    "terms",
    "cookie-policy",
    "disclaimer",
    "guides"
  ]) {
    const response = await page.goto(`/en/${route}`);
    expect(response?.status(), route).toBe(200);
    await expect(page.locator("html")).toHaveAttribute("lang", "en");
    if (route === "contact") {
      await expect(page.getByText("help@example.test")).toBeVisible();
    }
  }
});

test("sitemap lists static routes with reciprocal alternates and excludes drafts", async ({
  page,
  request
}) => {
  await page.goto("/en/guides");
  await expect(page.getByRole("status")).toContainText("remain private");
  await expect(page.locator('a[href*="/guides/"]')).toHaveCount(0);

  const response = await request.get("/sitemap.xml");
  expect(response.status()).toBe(200);
  expect(response.headers()["content-type"]).toContain("application/xml");
  const xml = await response.text();
  const entries = [...xml.matchAll(/<url>\s*([\s\S]*?)\s*<\/url>/g)].map(
    (match) => match[1]
  );
  const locations = entries.map((entry) => {
    const location = entry.match(/<loc>([^<]+)<\/loc>/)?.[1];
    if (!location) throw new Error("Sitemap entry is missing a location.");
    return location;
  });
  const expectedLocations = SITEMAP_PATHS.flatMap((path) => [
    sitemapUrl("th", path),
    sitemapUrl("en", path)
  ]);

  expect(locations).toHaveLength(expectedLocations.length);
  expect(new Set(locations).size).toBe(locations.length);
  expect([...locations].sort()).toEqual([...expectedLocations].sort());

  const entriesByLocation = new Map(
    locations.map((location, index) => [location, entries[index]])
  );
  for (const path of SITEMAP_PATHS) {
    const expectedAlternates = {
      th: sitemapUrl("th", path),
      en: sitemapUrl("en", path),
      "x-default": sitemapUrl("th", path)
    };

    for (const locale of ["th", "en"] as const) {
      const entry = entriesByLocation.get(
        sitemapUrl(locale, path)
      );
      expect(entry).toBeDefined();
      const alternateLinks = [
        ...entry!.matchAll(/<xhtml:link\b([^>]*)\/>/g)
      ];
      expect(alternateLinks).toHaveLength(3);
      expect(
        Object.fromEntries(
          alternateLinks.map((link) => [
            readXmlAttribute(link[1], "hreflang"),
            readXmlAttribute(link[1], "href")
          ])
        )
      ).toEqual(expectedAlternates);
    }
  }

  expect(xml).not.toContain("/admin");
  expect(xml).not.toContain("daily-one-card-reading");
});

test("robots and ads.txt are generated from validated runtime configuration", async ({ request }) => {
  const robots = await request.get("/robots.txt");
  expect(robots.status()).toBe(200);
  const robotsText = await robots.text();
  expect(robotsText).toContain("Disallow: /admin");
  expect(robotsText).toContain(
    "Sitemap: https://tarot.example.test/sitemap.xml"
  );

  const ads = await request.get("/ads.txt");
  expect(ads.status()).toBe(200);
  expect(ads.headers()["content-type"]).toContain("text/plain");
  expect(await ads.text()).toBe(
    "google.com, pub-0000000000000001, DIRECT, f08c47fec0942fa0\n"
  );
});

test("admin is noindex and never contains public or Google integrations", async ({ page }) => {
  const requests: string[] = [];
  page.on("request", (request) => {
    const host = new URL(request.url()).hostname;
    if (GOOGLE_HOST.test(host)) requests.push(request.url());
  });
  await page.goto("/admin");
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute(
    "content",
    /noindex/
  );
  await expect(page.locator('meta[name="google-adsense-account"]')).toHaveCount(0);
  await expect(page.locator('[data-consent-settings]')).toHaveCount(0);
  expect(requests).toEqual([]);
});

test("regional consent settings reflect rewarded advertising and fail closed outside reviewed regions", async ({ browser }) => {
  for (const country of ["TH", "US", "DE"]) {
    const context = await browser.newContext({
      extraHTTPHeaders: { "CF-IPCountry": country }
    });
    const page = await context.newPage();
    await page.goto("/en/privacy");
    const dialog = page.getByRole("dialog");
    if (country !== "TH") await page.locator("[data-consent-settings]").click();
    await expect(dialog).toBeVisible();
    if (country === "TH") {
      await expect(dialog).toContainText("optional Google Ad Manager rewarded ads");
    } else if (country === "US") {
      await expect(dialog).toContainText("unavailable for this region");
    } else {
      await expect(dialog).toContainText("published guide pages");
    }
    await context.close();
  }
});

test("public shell remains keyboard accessible and does not overflow mobile or desktop", async ({ page }) => {
  for (const viewport of [
    { width: 360, height: 800 },
    { width: 1440, height: 900 }
  ]) {
    await page.setViewportSize(viewport);
    await page.goto("/en");
    await page.keyboard.press("Tab");
    await expect(page.locator(":focus")).toHaveAttribute("href", "#main-content");
    const overflows = await page.evaluate(
      () => document.documentElement.scrollWidth > window.innerWidth + 1
    );
    expect(overflows, `${viewport.width}px layout`).toBe(false);
  }
});

test("immersive shop keeps a direct accessible path to the existing Tarot client", async ({ page }) => {
  await page.goto("/en");
  const shop = page.getByRole("region", { name: "Interactive three-dimensional Tarot consultation room" });
  await expect(shop).toBeVisible();
  await expect(shop.getByText("Step inside the destiny shop")).toBeVisible();

  await shop.getByRole("button", { name: "Start Tarot now" }).click();
  await expect(page.locator("#tarot-consultation")).toBeFocused();
  await expect(page.getByRole("heading", { name: "Choose, reveal, reflect." })).toBeVisible();
  await expect(page.getByRole("button", { name: "Shuffle Deck" })).toBeEnabled();
});

test("visitor can walk to the 3D advisor and enter the API-backed consultation", async ({ page }) => {
  await page.goto("/en");
  await page.getByRole("button", { name: "Enter 3D shop" }).click();
  await expect(page.getByTestId("destiny-shop-canvas")).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText("Preparing the reception, gallery, and Tarot room…")).toBeHidden({ timeout: 15_000 });

  await page.keyboard.down("w");
  await page.waitForTimeout(3_700);
  await page.keyboard.up("w");
  await expect(page.getByRole("status").filter({ hasText: "The Tarot advisor is ready" })).toBeVisible();

  await page.getByRole("button", { name: "Sit for a Tarot reading" }).click();
  await expect(page.locator("#tarot-consultation")).toBeFocused();
  await expect(page.getByRole("button", { name: "Shuffle Deck" })).toBeEnabled();
});
