import { expect, test } from "@playwright/test";

// Also run against the HTTPS edge with PLAYWRIGHT_BASE_URL to catch redirects
// that leak Docker's internal listener port instead of preserving the origin.
for (const locale of ["th", "en"] as const) {
  for (const slash of ["", "/"]) {
    test(`locale route /${locale}${slash} preserves origin and query`, async ({ request, baseURL }) => {
      const original = new URL(`/${locale}${slash}?source=locale-test&value=a%2Fb`, baseURL);
      let current = original;
      let redirects = 0;
      while (true) {
        const response = await request.get(current.href, { maxRedirects: 0, timeout: 15_000 });
        if ([301, 302, 303, 307, 308].includes(response.status())) {
          expect(++redirects, "locale redirects must not loop").toBeLessThanOrEqual(1);
          const location = response.headers().location;
          expect(location).toBeTruthy();
          current = new URL(location, current);
          expect(current.origin, "redirect must preserve public scheme, hostname and port").toBe(original.origin);
          expect(current.pathname).toBe(`/${locale}/`);
          expect(current.search).toBe(original.search);
          continue;
        }
        expect(response.status()).toBe(200);
        expect(await response.text()).toMatch(new RegExp(`<html[^>]*\\blang="${locale}"`));
        break;
      }
    });
  }
}
