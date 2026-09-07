import { expect, test } from "@playwright/test";

// Run against `next dev` as well as the production server: Strict Mode replays
// setup/cleanup in development and previously left a freed Rapier controller in use.
test("collision controller survives entry, movement, exit and re-entry", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto("/en");
  for (let visit = 0; visit < 3; visit++) {
    await page.getByRole("button", { name: "Enter 3D shop" }).click();
    await expect(page.getByText("Preparing the reception, gallery, and Tarot room…")).toBeHidden({ timeout: 20_000 });
    await expect(page.locator(".shop-zone-indicator")).toContainText("Entrance");
    await page.keyboard.down("w");
    try { await expect(page.locator(".shop-zone-indicator")).toContainText("Service gallery", { timeout: 10_000 }); }
    finally { await page.keyboard.up("w"); }
    await page.getByRole("button", { name: "Exit 3D view" }).click();
    await expect(page.getByTestId("destiny-shop-canvas")).toHaveCount(0);
  }
  expect(errors).toEqual([]);
});

test("GLB assets load only after entry, render all zones, and survive quality changes", async ({ page }) => {
  const loaded: string[] = [], errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("response", response => {
    if (response.url().endsWith(".glb") && response.ok()) loaded.push(response.url().split("/").pop()!);
  });
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto("/en");
  await expect(page.getByRole("button", { name: "Enter 3D shop" })).toBeEnabled();
  expect(loaded).toEqual([]);
  await page.getByRole("button", { name: "Enter 3D shop" }).click();
  await expect(page.getByText("Preparing the reception, gallery, and Tarot room…")).toBeHidden({ timeout: 15_000 });
  expect(loaded.sort()).toEqual(["advisor.glb", "shop.glb", "tarot-back.glb", "visitor.glb"]);
  await page.screenshot({ path: ".tmp/model-previews/in-shop-entrance.png" });
  await page.keyboard.down("w");
  try { await expect(page.locator(".shop-zone-indicator")).toContainText("Service gallery", { timeout: 10_000 }); }
  finally { await page.keyboard.up("w"); }
  await page.screenshot({ path: ".tmp/model-previews/in-shop-gallery.png" });
  await page.keyboard.down("w");
  try { await expect(page.getByRole("button", { name: "Sit for a Tarot reading" })).toBeEnabled({ timeout: 10_000 }); }
  finally { await page.keyboard.up("w"); }
  await expect(page.locator(".shop-zone-indicator")).toContainText("Private Tarot room");
  await page.keyboard.down("w");
  await page.waitForTimeout(1_000);
  await page.keyboard.up("w");
  for (const quality of ["LOW", "STANDARD", "HIGH"]) {
    await page.getByLabel("Visual quality").selectOption(quality);
    await expect(page.getByTestId("destiny-shop-canvas").locator("canvas")).toBeVisible();
  }
  await page.screenshot({ path: ".tmp/model-previews/in-shop-tarot-room.png" });
  await page.getByRole("button", { name: "Sit for a Tarot reading" }).click();
  await expect(page.getByRole("region", { name: "Private Tarot consultation", exact: true })).toBeVisible();
  expect(errors).toEqual([]);
});

test("a missing model returns to the accessible reading without losing the question", async ({ page }) => {
  await page.route("**/models/destiny-shop/initial/advisor.glb", route => route.fulfill({ status: 404, body: "Missing model" }));
  await page.goto("/en");
  await page.getByRole("button", { name: "Ask a Question" }).click();
  const question = page.locator("textarea").first();
  await question.fill("What should I focus on this week?");
  await page.getByRole("button", { name: "Enter 3D shop" }).click();
  await expect(page.getByText("The 3D view stopped unexpectedly.", { exact: false })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId("destiny-shop-canvas")).toHaveCount(0);
  await expect(question).toHaveValue("What should I focus on this week?");
  await expect(page.getByRole("button", { name: "Shuffle Deck" })).toBeEnabled();
});

test("WebGL context loss preserves the direct reading fallback", async ({ page }) => {
  await page.goto("/en");
  await page.getByRole("button", { name: "Enter 3D shop" }).click();
  await expect(page.getByText("Preparing the reception, gallery, and Tarot room…")).toBeHidden({ timeout: 15_000 });
  await page.getByTestId("destiny-shop-canvas").locator("canvas").evaluate(canvas => {
    canvas.dispatchEvent(new Event("webglcontextlost", { cancelable: true }));
  });
  await expect(page.getByText("The 3D view stopped unexpectedly.", { exact: false })).toBeVisible();
  await expect(page.getByRole("button", { name: "Shuffle Deck" })).toBeEnabled();
});
