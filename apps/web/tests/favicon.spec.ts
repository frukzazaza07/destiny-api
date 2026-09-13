import { expect, test } from "@playwright/test";

for (const path of ["/th", "/th/", "/en", "/en/"]) {
  test(`branded icons on ${path}`, async ({ page, request }) => {
    await page.goto(path);
    const icons = page.locator('link[rel="icon"]');
    await expect(icons.first()).toBeAttached();
    const links = await page.locator('link[rel="icon"], link[rel="apple-touch-icon"]')
      .evaluateAll((nodes) => nodes.map((node) => ({
        href: (node as HTMLLinkElement).href,
        sizes: node.getAttribute("sizes")
      })));
    expect(links.some(({ href }) => new URL(href).pathname === "/favicon.ico")).toBe(true);
    expect(links.some(({ href }) => new URL(href).pathname === "/icon.svg")).toBe(true);
    expect(links.some(({ href, sizes }) => new URL(href).pathname === "/apple-icon.png" && sizes === "180x180")).toBe(true);
    for (const { href } of links) {
      const response = await request.get(href);
      expect(response.status()).toBe(200);
      expect(response.headers()["content-type"]).toMatch(/^image\//);
    }
    for (const size of [16, 32, 48]) {
      const response = await request.get(`/favicon-${size}x${size}.png`);
      expect(response.status()).toBe(200);
      const png = await response.body();
      expect(png.readUInt32BE(16)).toBe(size);
      expect(png.readUInt32BE(20)).toBe(size);
    }
  });
}
