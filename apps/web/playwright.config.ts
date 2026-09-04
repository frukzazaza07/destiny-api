import { defineConfig } from "@playwright/test";

const port = 3110;
const externalBaseUrl = process.env.PLAYWRIGHT_BASE_URL?.trim();

export default defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  timeout: 30_000,
  use: {
    baseURL: externalBaseUrl || `http://127.0.0.1:${port}`,
    channel: "chrome",
    headless: true,
    trace: "retain-on-failure"
  },
  webServer: externalBaseUrl ? undefined : {
    command: "node node_modules/next/dist/bin/next start --hostname 127.0.0.1",
    url: `http://127.0.0.1:${port}/th`,
    reuseExistingServer: false,
    timeout: 60_000,
    env: {
      ...process.env,
      HOSTNAME: "127.0.0.1",
      PORT: String(port),
      SITE_URL: "https://tarot.example.test",
      CONTACT_EMAIL: "help@example.test",
      ADSENSE_CLIENT_ID: "ca-pub-0000000000000001",
      ADSENSE_GUIDE_SLOT_ID: "0000000001",
      ADSENSE_ENABLED: "false",
      GA_MEASUREMENT_ID: "G-TEST000001",
      ANALYTICS_ENABLED: "false",
      CF_IPCOUNTRY_TRUSTED: "true"
    }
  }
});
